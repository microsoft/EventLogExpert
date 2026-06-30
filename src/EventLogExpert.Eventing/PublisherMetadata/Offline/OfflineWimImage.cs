// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;
using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

/// <summary>
///     The result of
///     <see cref="OfflineWimImage.TryExtractAsync(string, int, string, ITraceLogger?, CancellationToken)" />: the outcome
///     plus the extracted (disposable) image on success.
/// </summary>
public readonly record struct OfflineWimExtractResult(OfflineWimExtractStatus Status, OfflineWimImage? Image)
{
    internal static OfflineWimExtractResult Failed(OfflineWimExtractStatus status) => new(status, null);
}

/// <summary>
///     Extracts a single image from a foreign Windows <c>.wim</c>/<c>.esd</c> to a temp folder so the existing
///     offline Directory source can read it. A DISM/WIM filter MOUNT holds the registry hives so no file IO can copy them;
///     this EXTRACTS via <c>WIMApplyImage</c> instead, producing a plain tree whose <c>\Windows\System32\config\...</c>
///     layout matches a mounted volume. The extracted copy is deleted on <see cref="Dispose" />. The static entry points
///     NEVER throw for a bad / corrupt / locked / non-WIM image - they return a typed
///     <see cref="OfflineWimExtractStatus" />.
/// </summary>
public sealed class OfflineWimImage : IDisposable
{
    private const FileAttributes UndeletableAttributes = FileAttributes.ReadOnly | FileAttributes.System | FileAttributes.Hidden;

    private readonly ITraceLogger? _logger;
    private readonly Mutex? _ownershipBeacon;

    private bool _disposed;

    private OfflineWimImage(string extractedRoot, Mutex? ownershipBeacon, ITraceLogger? logger)
    {
        ExtractedRoot = extractedRoot;
        _ownershipBeacon = ownershipBeacon;
        _logger = logger;
    }

    /// <summary>Root of the extracted image; pass to the offline Directory source. Valid until <see cref="Dispose" />.</summary>
    public string ExtractedRoot { get; }

    /// <summary>
    ///     Reads the image-index metadata from <paramref name="wimPath" /> so a caller can list / validate
    ///     <c>--wim-index</c>. Needs no elevation and never throws for a bad image.
    /// </summary>
    public static WimImageList ReadIndexList(string wimPath, ITraceLogger? logger) =>
        ReadIndexList(wimPath, WimOperations.Instance, logger);

    /// <summary>
    ///     Deletes WIM extraction folders in the scratch root left by a crashed or self-terminated prior run, identified
    ///     by a dead ownership beacon so a live sibling's in-progress extraction is never removed. Helper-side startup
    ///     maintenance paired with <see cref="OfflineRegistryHive" />'s orphaned-mount sweep.
    /// </summary>
    public static void ReconcileOrphanedExtractions(ITraceLogger? logger) =>
        ReconcileOrphanedExtractions(OfflineScratch.Root, logger);

    /// <summary>
    ///     Extracts the 1-based <paramref name="imageIndex" /> of <paramref name="wimPath" /> into a fresh folder under
    ///     <paramref name="tempParent" /> and returns it as an <see cref="OfflineWimImage" />. The apply requires
    ///     administrator privileges; index validation does not.
    /// </summary>
    public static Task<OfflineWimExtractResult> TryExtractAsync(
        string wimPath, int imageIndex, string tempParent, ITraceLogger? logger, CancellationToken cancellationToken) =>
        TryExtractAsync(wimPath, imageIndex, tempParent, WimOperations.Instance, logger, cancellationToken);

    public void Dispose()
    {
        if (_disposed) { return; }

        _disposed = true;

        TryDeleteExtraction(ExtractedRoot, _logger);

        // Release the liveness beacon only AFTER the folder is gone, so a concurrent sweep never sees a dead beacon while
        // the folder still exists (which would be a redundant but harmless re-delete attempt).
        _ownershipBeacon?.Dispose();
    }

    internal static WimImageList ReadIndexList(string wimPath, IWimOperations nativeApi, ITraceLogger? logger)
    {
        if (File.Exists(wimPath))
        {
            return nativeApi.ReadImageList(wimPath, logger);
        }

        logger?.Debug($"{nameof(OfflineWimImage)}: WIM file not found: {wimPath}.");

        return WimImageList.NotAWim;

    }

    internal static void ReconcileOrphanedExtractions(string scratchRoot, ITraceLogger? logger)
    {
        if (!Directory.Exists(scratchRoot)) { return; }

        try
        {
            foreach (string directory in Directory.EnumerateDirectories(scratchRoot, "ELX_WIM_*"))
            {
                string name = Path.GetFileName(directory);

                if (OwnershipBeacon.IsAlive(name)) { continue; }

                logger?.Debug($"{nameof(OfflineWimImage)}: reclaiming orphaned extraction {directory}.");
                TryDeleteExtraction(directory, logger);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            logger?.Debug($"{nameof(OfflineWimImage)}: orphaned-extraction sweep failed: {ex.Message}");
        }
    }

    internal static async Task<OfflineWimExtractResult> TryExtractAsync(
        string wimPath,
        int imageIndex,
        string tempParent,
        IWimOperations nativeApi,
        ITraceLogger? logger,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return OfflineWimExtractResult.Failed(OfflineWimExtractStatus.Cancelled);
        }

        WimImageList imageList = ReadIndexList(wimPath, nativeApi, logger);

        if (imageList.Status != WimImageListStatus.Ok)
        {
            return OfflineWimExtractResult.Failed(OfflineWimExtractStatus.NotAWim);
        }

        WimImageEntry? entry = imageList.Images.FirstOrDefault(image => image.Index == imageIndex);

        if (entry is null)
        {
            logger?.Debug($"{nameof(OfflineWimImage)}: image index {imageIndex} is not in {wimPath} (it has {imageList.Images.Count}).");

            return OfflineWimExtractResult.Failed(OfflineWimExtractStatus.IndexOutOfRange);
        }

        if (entry.TotalBytes is { } requiredBytes && !HasEnoughFreeSpace(tempParent, requiredBytes, logger))
        {
            return OfflineWimExtractResult.Failed(OfflineWimExtractStatus.InsufficientSpace);
        }

        if (!nativeApi.IsProcessElevated())
        {
            logger?.Debug($"{nameof(OfflineWimImage)}: extracting a WIM image requires administrator privileges.");

            return OfflineWimExtractResult.Failed(OfflineWimExtractStatus.NeedsElevation);
        }

        // A SHORT root keeps the deep WinSxS tree under MAX_PATH; the GUID makes it unique across concurrent runs.
        string extractDirName = $"ELX_WIM_{Guid.NewGuid():N}";
        string extractRoot = Path.Combine(tempParent, extractDirName);

        // Publish a liveness beacon BEFORE the long apply so a concurrent run's reconciliation sweep never deletes this
        // extraction mid-flight; on a hard self-terminate the OS releases it and the next run's sweep reclaims the folder.
        Mutex? ownershipBeacon = OwnershipBeacon.TryCreate(extractDirName, logger);

        if (ownershipBeacon is null)
        {
            // Fail closed (mirrors OfflineRegistryHive's dirty-hive path): without a liveness beacon a concurrent
            // reconciliation sweep would see no live owner for this ELX_WIM_* folder and could delete it mid-apply. Refuse
            // to create the folder or start the apply; nothing to clean up because the directory was never created.
            logger?.Error($"{nameof(OfflineWimImage)}: could not acquire an ownership beacon for {extractDirName}; refusing to extract without sweep protection against concurrent reconciliation.");

            return OfflineWimExtractResult.Failed(OfflineWimExtractStatus.ApplyFailed);
        }

        try
        {
            Directory.CreateDirectory(extractRoot);

            logger?.Information($"Extracting image index {imageIndex} from {wimPath} (this can take several minutes)...");

            // The apply is a long blocking native call; run it off the current thread. Cancellation is honored INSIDE the
            // apply via the message-callback abort (mapped to ERROR_REQUEST_ABORTED), so Task.Run itself is not cancelled.
            int applyResult = await Task.Run(
                () => nativeApi.ApplyImage(wimPath, imageIndex, extractRoot, tempParent, cancellationToken, logger),
                CancellationToken.None);

            switch (applyResult)
            {
                case Win32ErrorCodes.ERROR_SUCCESS:
                    logger?.Information($"Extracted image index {imageIndex} to {extractRoot}.");

                    // The returned image now owns the beacon + folder; both are released on its Dispose.
                    return new OfflineWimExtractResult(
                        OfflineWimExtractStatus.Extracted, new OfflineWimImage(extractRoot, ownershipBeacon, logger));
                case Win32ErrorCodes.ERROR_REQUEST_ABORTED:
                    CleanupFailedExtraction(extractRoot, ownershipBeacon, logger);

                    return OfflineWimExtractResult.Failed(OfflineWimExtractStatus.Cancelled);
                case Win32ErrorCodes.ERROR_DISK_FULL:
                    CleanupFailedExtraction(extractRoot, ownershipBeacon, logger);

                    return OfflineWimExtractResult.Failed(OfflineWimExtractStatus.InsufficientSpace);
                default:
                    logger?.Debug($"{nameof(OfflineWimImage)}: WIMApplyImage failed for index {imageIndex} of {wimPath} (error {applyResult}).");
                    CleanupFailedExtraction(extractRoot, ownershipBeacon, logger);

                    return OfflineWimExtractResult.Failed(OfflineWimExtractStatus.ApplyFailed);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            logger?.Debug($"{nameof(OfflineWimImage)}: extracting index {imageIndex} of {wimPath} failed: {ex.Message}");
            CleanupFailedExtraction(extractRoot, ownershipBeacon, logger);

            return OfflineWimExtractResult.Failed(OfflineWimExtractStatus.ApplyFailed);
        }
    }

    // Releases the resources of a failed extraction: the partial folder first, then the beacon (mirrors Dispose ordering).
    private static void CleanupFailedExtraction(string extractRoot, Mutex? ownershipBeacon, ITraceLogger? logger)
    {
        TryDeleteExtraction(extractRoot, logger);
        ownershipBeacon?.Dispose();
    }

    // An extracted Windows tree contains JUNCTIONS (e.g. the legacy "Users\All Users" -> ProgramData with deny-list
    // ACLs); a reparse point is deleted as a LINK and NEVER recursed into, so the walk neither follows a junction out of
    // the tree nor trips its deny ACL. The read-only/system/hidden attributes WIMApplyImage restores are cleared first
    // (Directory.Delete throws on a read-only file). Children are materialized before deletion to avoid mutating a live
    // enumerator.
    private static void DeleteDirectoryTree(DirectoryInfo directory)
    {
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            directory.Delete();

            return;
        }

        foreach (FileInfo file in directory.GetFiles())
        {
            if ((file.Attributes & UndeletableAttributes) != 0) { file.Attributes &= ~UndeletableAttributes; }

            file.Delete();
        }

        foreach (DirectoryInfo subDirectory in directory.GetDirectories())
        {
            DeleteDirectoryTree(subDirectory);
        }

        if ((directory.Attributes & FileAttributes.ReadOnly) != 0) { directory.Attributes &= ~FileAttributes.ReadOnly; }

        directory.Delete();
    }

    private static bool HasEnoughFreeSpace(string tempParent, long requiredBytes, ITraceLogger? logger)
    {
        try
        {
            if (requiredBytes < 0) { return false; }

            string root = Path.GetPathRoot(Path.GetFullPath(tempParent)) ?? tempParent;
            long available = new DriveInfo(root).AvailableFreeSpace;

            // Overflow-safe: a malformed/huge TotalBytes must FAIL the check, not wrap past long.MaxValue into a pass.
            // available - requiredBytes cannot overflow once available >= requiredBytes (both non-negative).
            long headroom = requiredBytes / 10;

            if (available >= requiredBytes && available - requiredBytes >= headroom) { return true; }

            logger?.Debug($"{nameof(OfflineWimImage)}: {root} has {available} bytes free, needs ~{requiredBytes} plus headroom.");

            return false;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // If free space cannot be determined, do not block on the precheck; the apply's own ERROR_DISK_FULL backstops.
            logger?.Debug($"{nameof(OfflineWimImage)}: could not check free space for {tempParent}: {ex.Message}");

            return true;
        }
    }

    // Mirrors DeleteDirectoryTree: never recurse into reparse points - a junction in an extracted Windows tree would
    // otherwise loop, leave the root, or throw access-denied - so the leaked-size estimate stays best-effort and bounded.
    private static long SumFilesSkippingReparsePoints(DirectoryInfo directory)
    {
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) { return 0; }

        long total = 0;

        foreach (FileInfo file in directory.GetFiles()) { total += file.Length; }

        foreach (DirectoryInfo subDirectory in directory.GetDirectories()) { total += SumFilesSkippingReparsePoints(subDirectory); }

        return total;
    }

    // Recursively deletes an extracted image, logging a Warning naming the leaked path + size on failure so a multi-GB
    // temp is visible rather than silently abandoned.
    private static void TryDeleteExtraction(string extractRoot, ITraceLogger? logger)
    {
        if (!Directory.Exists(extractRoot)) { return; }

        try
        {
            DeleteDirectoryTree(new DirectoryInfo(extractRoot));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            logger?.Warning($"{nameof(OfflineWimImage)}: could not delete extracted image at {extractRoot} (~{TryGetDirectorySize(extractRoot)} bytes leaked): {ex.Message}");
        }
    }

    private static long TryGetDirectorySize(string directory)
    {
        try
        {
            return SumFilesSkippingReparsePoints(new DirectoryInfo(directory));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return -1;
        }
    }
}
