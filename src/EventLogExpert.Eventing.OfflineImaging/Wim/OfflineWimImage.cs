// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.OfflineImaging.Interop;
using EventLogExpert.Eventing.OfflineImaging.Workspace;
using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Eventing.OfflineImaging.Wim;

public readonly record struct OfflineWimExtractResult(OfflineWimExtractStatus Status, OfflineWimImage? Image)
{
    internal static OfflineWimExtractResult Failed(OfflineWimExtractStatus status) => new(status, null);
}

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

    public string ExtractedRoot { get; }

    public static WimImageList ReadIndexList(string wimPath, ITraceLogger? logger) =>
        ReadIndexList(wimPath, WimOperations.Instance, logger);

    // Dead ownership beacons identify crashed-run extraction folders; live sibling folders are never reclaimed.
    public static void ReconcileOrphanedExtractions(ITraceLogger? logger) =>
        ReconcileOrphanedExtractions(OfflineScratch.Root, logger);

    // Applying requires elevation; image-index validation does not.
    public static Task<OfflineWimExtractResult> TryExtractAsync(
        string wimPath, int imageIndex, string tempParent, ITraceLogger? logger, CancellationToken cancellationToken) =>
        TryExtractAsync(wimPath, imageIndex, tempParent, WimOperations.Instance, logger, cancellationToken);

    public void Dispose()
    {
        if (_disposed) { return; }

        _disposed = true;

        TryDeleteExtraction(ExtractedRoot, _logger);

        // Release the beacon after deleting the folder so a concurrent sweep never sees a dead owner for a live path.
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

        // Keep the root short for deep WinSxS paths; the GUID isolates concurrent runs.
        string extractDirName = $"ELX_WIM_{Guid.NewGuid():N}";
        string extractRoot = Path.Combine(tempParent, extractDirName);

        // Publish the beacon before apply so concurrent orphan sweeps never delete this extraction mid-flight.
        Mutex? ownershipBeacon = OwnershipBeacon.TryCreate(extractDirName, logger);

        if (ownershipBeacon is null)
        {
            // Fail closed: without a beacon, a concurrent sweep could delete the folder mid-apply.
            logger?.Error($"{nameof(OfflineWimImage)}: could not acquire an ownership beacon for {extractDirName}; refusing to extract without sweep protection against concurrent reconciliation.");

            return OfflineWimExtractResult.Failed(OfflineWimExtractStatus.ApplyFailed);
        }

        try
        {
            Directory.CreateDirectory(extractRoot);

            logger?.Information($"Extracting image index {imageIndex} from {wimPath} (this can take several minutes)...");

            // Cancellation is honored inside WIMApplyImage via the native callback, so Task.Run itself is not cancelled.
            int applyResult = await Task.Run(
                () => nativeApi.ApplyImage(wimPath, imageIndex, extractRoot, tempParent, cancellationToken, logger),
                CancellationToken.None);

            switch (applyResult)
            {
                case Win32ErrorCodes.ERROR_SUCCESS:
                    logger?.Information($"Extracted image index {imageIndex} to {extractRoot}.");

                    // Ownership transfers to OfflineWimImage; Dispose releases the folder and beacon.
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

    // Failed extractions delete the partial folder before releasing the beacon, matching Dispose ordering.
    private static void CleanupFailedExtraction(string extractRoot, Mutex? ownershipBeacon, ITraceLogger? logger)
    {
        TryDeleteExtraction(extractRoot, logger);
        ownershipBeacon?.Dispose();
    }

    // Delete reparse points as links and clear WIM-restored attributes so cleanup never follows junctions.
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

            // Overflow-safe: malformed huge TotalBytes must fail the headroom check, not wrap into a pass.
            long headroom = requiredBytes / 10;

            if (available >= requiredBytes && available - requiredBytes >= headroom) { return true; }

            logger?.Debug($"{nameof(OfflineWimImage)}: {root} has {available} bytes free, needs ~{requiredBytes} plus headroom.");

            return false;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // If the precheck cannot read free space, let WIMApplyImage report ERROR_DISK_FULL.
            logger?.Debug($"{nameof(OfflineWimImage)}: could not check free space for {tempParent}: {ex.Message}");

            return true;
        }
    }

    // Skip reparse points so the leaked-size estimate stays bounded inside the extracted tree.
    private static long SumFilesSkippingReparsePoints(DirectoryInfo directory)
    {
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) { return 0; }

        long total = 0;

        foreach (FileInfo file in directory.GetFiles()) { total += file.Length; }

        foreach (DirectoryInfo subDirectory in directory.GetDirectories()) { total += SumFilesSkippingReparsePoints(subDirectory); }

        return total;
    }

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
