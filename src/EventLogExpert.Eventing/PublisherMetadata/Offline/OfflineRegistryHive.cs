// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;
using EventLogExpert.Logging.Abstractions;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

/// <summary>
///     Loads an offline registry hive file (an image's <c>SOFTWARE</c> / <c>SYSTEM</c>) read-only via
///     <see cref="NativeMethods.RegLoadAppKey" /> and exposes its root as a managed <see cref="RegistryKey" /> so callers
///     navigate it with the normal registry API. The hive is first STAGED to a writable temp copy: RegLoadAppKey needs
///     write access to the hive file (it fails with access-denied on read-only media such as a mounted ISO/WIM), and a
///     dirty hive captured from a live/hibernated system must replay its <c>.LOG</c> sidecars - staging a writable copy
///     handles read-only media, dirty hives, and in-use locks uniformly. The staged copy is deleted on
///     <see cref="Dispose" />.
/// </summary>
internal sealed class OfflineRegistryHive : IDisposable
{
    private readonly string _stagingDirectory;

    private OfflineRegistryHive(RegistryKey root, string stagingDirectory)
    {
        Root = root;
        _stagingDirectory = stagingDirectory;
    }

    /// <summary>Root key of the loaded hive; navigate with <see cref="RegistryKey.OpenSubKey(string)" />.</summary>
    public RegistryKey Root { get; }

    public static OfflineRegistryHive? TryLoad(string hiveFilePath, ITraceLogger? logger)
    {
        if (!File.Exists(hiveFilePath))
        {
            logger?.Debug($"{nameof(OfflineRegistryHive)}: hive file not found: {hiveFilePath}.");

            return null;
        }

        string stagingDirectory = Path.Combine(Path.GetTempPath(), "elx_hive_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(stagingDirectory);

            string stagedHive = StageHive(hiveFilePath, stagingDirectory);

            int result = NativeMethods.RegLoadAppKey(stagedHive, out nint handle, NativeMethods.KEY_READ, 0, 0);

            if (result != 0)
            {
                logger?.Debug(
                    $"{nameof(OfflineRegistryHive)}: RegLoadAppKey failed for {hiveFilePath} (staged at {stagedHive}) with error {result}.");
                TryDeleteDirectory(stagingDirectory);

                return null;
            }

            // RegistryKey takes ownership of the handle; disposing it calls RegCloseKey, which auto-unloads the app hive.
            RegistryKey root = RegistryKey.FromHandle(new SafeRegistryHandle(handle, ownsHandle: true));

            return new OfflineRegistryHive(root, stagingDirectory);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            logger?.Debug($"{nameof(OfflineRegistryHive)}: failed to load hive {hiveFilePath}: {ex}");
            TryDeleteDirectory(stagingDirectory);

            return null;
        }
    }

    public void Dispose()
    {
        Root.Dispose();
        TryDeleteDirectory(_stagingDirectory);
    }

    private static void CopyWritable(string source, string destination)
    {
        File.Copy(source, destination, overwrite: true);

        // File.Copy preserves the source attributes; clear read-only so the staged hive is writable for RegLoadAppKey.
        File.SetAttributes(destination, FileAttributes.Normal);
    }

    // Copies the hive plus any .LOG/.LOG1/.LOG2 sidecars (needed for dirty-hive replay) into the staging directory,
    // clearing the read-only attribute the source may carry from read-only media so RegLoadAppKey can open it writable.
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
}
