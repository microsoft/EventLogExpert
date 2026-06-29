// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;
using EventLogExpert.Logging.Abstractions;
using Microsoft.Win32;

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

/// <summary>The result of <see cref="OfflineRegistryHive.TryLoad" />: the outcome plus the loaded hive on success.</summary>
internal readonly record struct OfflineHiveLoadResult(OfflineHiveLoadStatus Status, OfflineRegistryHive? Hive)
{
    internal static OfflineHiveLoadResult Failed(OfflineHiveLoadStatus status) => new(status, null);
}

/// <summary>
///     Loads an offline image registry hive file (an image's <c>SOFTWARE</c> / <c>SYSTEM</c>) and exposes its root as
///     a managed <see cref="RegistryKey" />. The hive is first STAGED to a writable temp copy (the source image may be
///     read-only and a dirty hive is modified during recovery). A clean hive loads via <c>RegLoadAppKey</c> (no admin). A
///     dirty hive captured from a live/imaged system carries pending dual-log (<c>.LOG1</c>/<c>.LOG2</c>) data that
///     <c>RegLoadAppKey</c> cannot replay (it fails with <see cref="Win32ErrorCodes.ERROR_BADDB" />); such a hive is
///     recovered via <c>RegLoadKey</c> under backup/restore privileges (administrator). The recovery mount lives under
///     <c>HKLM\ELX_&lt;pid&gt;_&lt;guid&gt;</c> and is tracked by a machine-global ownership mutex so a crashed process's
///     orphaned mount is reclaimed by the next run. The staged copy and any mount are released on <see cref="Dispose" />.
/// </summary>
internal sealed class OfflineRegistryHive : IDisposable
{
    private static readonly byte[] s_hiveSignature = "regf"u8.ToArray();
    private static readonly Lock s_sweepGate = new();

    private static bool s_sweptOrphanedMounts;

    private readonly ITraceLogger? _logger;
    private readonly string? _mountSubKey;
    private readonly IOfflineHiveNativeApi _nativeApi;
    private readonly Mutex? _ownershipMutex;
    private readonly string _stagingDirectory;

    private bool _disposed;

    private OfflineRegistryHive(
        RegistryKey root,
        string stagingDirectory,
        string? mountSubKey,
        Mutex? ownershipMutex,
        IOfflineHiveNativeApi nativeApi,
        ITraceLogger? logger)
    {
        Root = root;
        _stagingDirectory = stagingDirectory;
        _mountSubKey = mountSubKey;
        _ownershipMutex = ownershipMutex;
        _nativeApi = nativeApi;
        _logger = logger;
    }

    /// <summary>Root key of the loaded hive; navigate with <see cref="RegistryKey.OpenSubKey(string)" />.</summary>
    public RegistryKey Root { get; }

    public static OfflineHiveLoadResult TryLoad(string hiveFilePath, ITraceLogger? logger, IOfflineHiveNativeApi? nativeApi = null)
    {
        nativeApi ??= OfflineHiveNativeApi.Instance;

        if (!File.Exists(hiveFilePath))
        {
            logger?.Debug($"{nameof(OfflineRegistryHive)}: hive file not found: {hiveFilePath}.");

            return OfflineHiveLoadResult.Failed(OfflineHiveLoadStatus.NotAHive);
        }

        string mountSubKey = $"ELX_{Environment.ProcessId}_{Guid.NewGuid():N}";
        string stagingDirectory = Path.Combine(Path.GetTempPath(), mountSubKey);

        try
        {
            Directory.CreateDirectory(stagingDirectory);

            string stagedHive = StageHive(hiveFilePath, stagingDirectory);

            // A non-hive file must never reach the privileged recovery path; a real hive starts with the "regf" signature.
            // The probe stream is fully closed before any native load so it cannot cause a sharing violation.
            if (!HasHiveSignature(stagedHive))
            {
                logger?.Debug($"{nameof(OfflineRegistryHive)}: {hiveFilePath} is not a registry hive (no regf signature).");
                TryDeleteDirectory(stagingDirectory);

                return OfflineHiveLoadResult.Failed(OfflineHiveLoadStatus.NotAHive);
            }

            int appKeyResult = nativeApi.LoadApplicationHive(stagedHive, out var appKeyHandle);

            if (appKeyResult == Win32ErrorCodes.ERROR_SUCCESS && appKeyHandle is not null)
            {
                // Clean hive: the handle owns the hive (closing it auto-unloads), so there is no mount to track.
                RegistryKey root = RegistryKey.FromHandle(appKeyHandle);

                return new OfflineHiveLoadResult(
                    OfflineHiveLoadStatus.Loaded,
                    new OfflineRegistryHive(root, stagingDirectory, mountSubKey: null, ownershipMutex: null, nativeApi, logger));
            }

            appKeyHandle?.Dispose();

            // It IS a hive (regf) but RegLoadAppKey failed: a dirty hive needing dual-log recovery via RegLoadKey.
            return RecoverDirtyHive(stagedHive, stagingDirectory, mountSubKey, nativeApi, logger, appKeyResult);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            logger?.Debug($"{nameof(OfflineRegistryHive)}: failed to load hive {hiveFilePath}: {ex}");

            TryDeleteDirectory(stagingDirectory);

            return OfflineHiveLoadResult.Failed(OfflineHiveLoadStatus.RecoveryFailed);
        }
    }

    public void Dispose()
    {
        if (_disposed) { return; }

        _disposed = true;

        Root.Dispose();

        // A recovery mount (named subtree under HKLM) must be explicitly unloaded; a clean app-hive handle already
        // unloaded when Root closed above.
        if (_mountSubKey is not null)
        {
            UnloadMountedHive(_nativeApi, _mountSubKey, _logger);
            _ownershipMutex?.Dispose();
        }

        TryDeleteDirectory(_stagingDirectory);
    }

    private static void CopyWritable(string source, string destination)
    {
        File.Copy(source, destination, overwrite: true);

        // File.Copy preserves the source attributes; clear read-only so the staged hive is writable for the loaders.
        File.SetAttributes(destination, FileAttributes.Normal);
    }

    private static bool HasHiveSignature(string filePath)
    {
        Span<byte> header = stackalloc byte[s_hiveSignature.Length];

        using (FileStream stream = File.OpenRead(filePath))
        {
            if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length) { return false; }
        }

        return header.SequenceEqual(s_hiveSignature);
    }

    private static bool IsMountOwnerAlive(string mountSubKey)
    {
        try
        {
            using Mutex owner = Mutex.OpenExisting($"Global\\{mountSubKey}");

            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // The beacon exists but is not openable by us; treat as alive (do not reclaim someone else's live mount).
            return true;
        }
    }

    private static OfflineHiveLoadResult RecoverDirtyHive(
        string stagedHive,
        string stagingDirectory,
        string mountSubKey,
        IOfflineHiveNativeApi nativeApi,
        ITraceLogger? logger,
        int appKeyResult)
    {
        logger?.Debug($"{nameof(OfflineRegistryHive)}: RegLoadAppKey failed (error {appKeyResult}); attempting dual-log recovery via RegLoadKey.");

        SweepOrphanedMountsOnce(nativeApi, logger);

        // Publish the ownership beacon BEFORE RegLoadKey makes the ELX_ mount visible under HKLM. Were the beacon
        // created after the mount, a concurrent sibling process's orphan sweep could observe the new mount before its
        // beacon exists, judge it ownerless, and unload it mid-load. The beacon is held only as an open handle, so a
        // hard crash auto-releases it and the next run's sweep reclaims this mount.
        Mutex? ownershipMutex = TryCreateOwnershipBeacon(mountSubKey, logger);

        if (ownershipMutex is null)
        {
            // Fail closed: without a beacon a concurrent sibling sweep would judge this mount ownerless and could unload
            // it mid-use, so abort recovery rather than create an unprotected mount.
            logger?.Error($"{nameof(OfflineRegistryHive)}: cannot publish the ownership beacon for {mountSubKey}; aborting recovery to avoid a concurrent sweep unloading a live mount.");
            TryDeleteDirectory(stagingDirectory);

            return OfflineHiveLoadResult.Failed(OfflineHiveLoadStatus.RecoveryFailed);
        }

        using (IDisposable? privilege = nativeApi.TryEnterRecoveryPrivilege(logger))
        {
            if (privilege is null)
            {
                ownershipMutex?.Dispose();
                TryDeleteDirectory(stagingDirectory);

                return OfflineHiveLoadResult.Failed(OfflineHiveLoadStatus.NeedsElevation);
            }

            int loadResult = nativeApi.LoadHiveForRecovery(mountSubKey, stagedHive);

            if (loadResult != Win32ErrorCodes.ERROR_SUCCESS)
            {
                logger?.Debug($"{nameof(OfflineRegistryHive)}: RegLoadKey recovery failed for {mountSubKey} (error {loadResult}).");

                ownershipMutex?.Dispose();
                TryDeleteDirectory(stagingDirectory);

                return OfflineHiveLoadResult.Failed(OfflineHiveLoadStatus.RecoveryFailed);
            }
        }

        // The mount succeeded and its ownership beacon is already published. Open the root OUTSIDE the privileged section.
        try
        {
            RegistryKey? root = nativeApi.OpenMountedRoot(mountSubKey);

            if (root is null)
            {
                logger?.Debug($"{nameof(OfflineRegistryHive)}: recovery mount {mountSubKey} opened no root.");
                UnloadMountedHive(nativeApi, mountSubKey, logger);
                ownershipMutex?.Dispose();
                TryDeleteDirectory(stagingDirectory);

                return OfflineHiveLoadResult.Failed(OfflineHiveLoadStatus.RecoveryFailed);
            }

            return new OfflineHiveLoadResult(
                OfflineHiveLoadStatus.Loaded,
                new OfflineRegistryHive(root, stagingDirectory, mountSubKey, ownershipMutex, nativeApi, logger));
        }
        catch
        {
            UnloadMountedHive(nativeApi, mountSubKey, logger);
            ownershipMutex?.Dispose();
            TryDeleteDirectory(stagingDirectory);

            throw;
        }
    }

    // Copies the hive plus any .LOG/.LOG1/.LOG2 sidecars (needed for dirty-hive dual-log replay) into the staging
    // directory, clearing the read-only attribute the source may carry from read-only media so the loaders can open it.
    private static string StageHive(string hiveFilePath, string stagingDirectory)
    {
        string fileName = Path.GetFileName(hiveFilePath);
        string stagedHive = Path.Combine(stagingDirectory, fileName);
        CopyWritable(hiveFilePath, stagedHive);

        string[] logSuffixes = [".LOG", ".LOG1", ".LOG2"];

        foreach (string suffix in logSuffixes)
        {
            string sidecar = hiveFilePath + suffix;

            if (File.Exists(sidecar))
            {
                CopyWritable(sidecar, Path.Combine(stagingDirectory, fileName + suffix));
            }
        }

        return stagedHive;
    }

    // Reclaims HKLM mounts left by a crashed prior run: a mount is orphaned when its Global ownership mutex no longer
    // exists (the owner died and the OS released the handle). A live owner's mutex still opens, so a recycled PID or a
    // concurrent sibling app (CLI vs UI) is never reclaimed out from under a live process.
    private static void SweepOrphanedMountsOnce(IOfflineHiveNativeApi nativeApi, ITraceLogger? logger)
    {
        lock (s_sweepGate)
        {
            if (s_sweptOrphanedMounts) { return; }

            s_sweptOrphanedMounts = true;
        }

        try
        {
            foreach (string mountSubKey in nativeApi.EnumerateHklmSubKeyNames())
            {
                if (!mountSubKey.StartsWith("ELX_", StringComparison.Ordinal) || IsMountOwnerAlive(mountSubKey)) { continue; }

                logger?.Debug($"{nameof(OfflineRegistryHive)}: reclaiming orphaned recovery mount {mountSubKey}.");
                UnloadMountedHive(nativeApi, mountSubKey, logger);
                TryDeleteDirectory(Path.Combine(Path.GetTempPath(), mountSubKey));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            logger?.Debug($"{nameof(OfflineRegistryHive)}: orphan-mount sweep failed: {ex.Message}");

            // Re-arm so a later load in this process can retry: a transient failure should not leak orphaned mounts
            // until the next process run.
            lock (s_sweepGate) { s_sweptOrphanedMounts = false; }
        }
    }

    // A machine-global ownership beacon (an open, unowned named mutex) so the next run's sweep can tell this mount has a
    // live owner. Returns null if it cannot be created; recovery then fails closed (an unprotected mount could be unloaded
    // mid-use by a concurrent sweep) rather than proceeding without crash-reclaim protection.
    private static Mutex? TryCreateOwnershipBeacon(string mountSubKey, ITraceLogger? logger)
    {
        try
        {
            return new Mutex(initiallyOwned: false, $"Global\\{mountSubKey}");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            logger?.Debug($"{nameof(OfflineRegistryHive)}: could not create ownership beacon for {mountSubKey}: {ex.Message}");

            return null;
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup of the staging copy; a leftover temp dir is not fatal.
        }
    }

    /// <summary>
    ///     The SINGLE caller of <c>RegUnLoadKey</c>: enters the backup/restore privilege, unmounts the hive (retrying
    ///     with a GC between attempts to release any registry key a caller failed to dispose), then reverts the privilege.
    ///     Used by <see cref="Dispose" />, the recovery partial-failure path, and the orphan sweep, so every unmount is
    ///     privileged and bounded.
    /// </summary>
    private static void UnloadMountedHive(IOfflineHiveNativeApi nativeApi, string mountSubKey, ITraceLogger? logger)
    {
        using IDisposable? privilege = nativeApi.TryEnterRecoveryPrivilege(logger);

        if (privilege is null)
        {
            logger?.Error($"{nameof(OfflineRegistryHive)}: cannot unmount {mountSubKey} - backup/restore privilege is unavailable; mount leaked until reclaimed.");

            return;
        }

        for (var attempt = 1; ; attempt++)
        {
            int result = nativeApi.UnloadHive(mountSubKey);

            if (result == Win32ErrorCodes.ERROR_SUCCESS) { return; }

            if (attempt >= 3)
            {
                logger?.Error($"{nameof(OfflineRegistryHive)}: failed to unmount {mountSubKey} after {attempt} attempts (error {result}); it will be reclaimed on a later run.");

                return;
            }

            // A still-open key under the mount blocks the unload; force finalization of any leaked RegistryKey, then retry.
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}
