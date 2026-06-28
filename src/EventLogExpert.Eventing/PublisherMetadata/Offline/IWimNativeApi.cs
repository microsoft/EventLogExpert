// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

/// <summary>
///     The native WIM operations <see cref="OfflineWimImage" /> orchestrates, behind an interface so the
///     read/validate/ extract/cancel state machine can be unit-tested with a fake that returns crafted image lists and
///     Win32 results - the real <c>WIMApplyImage</c> path needs administrator privileges and a real multi-GB image. The
///     seam is COARSE on purpose: no native handle ever crosses it, so the fake needs no real <c>wimgapi</c> handle, and
///     the handle + message-callback lifetime stays entirely inside <see cref="WimNativeApi" />.
/// </summary>
internal interface IWimNativeApi
{
    /// <summary>
    ///     Applies (extracts) the 1-based <paramref name="imageIndex" /> of <paramref name="wimPath" /> into
    ///     <paramref name="destinationDirectory" />, using <paramref name="scratchDirectory" /> for WIMGAPI scratch. Returns a
    ///     Win32 error code (0 = success; <c>ERROR_REQUEST_ABORTED</c> when cancelled). Requires elevation.
    /// </summary>
    int ApplyImage(
        string wimPath,
        int imageIndex,
        string destinationDirectory,
        string scratchDirectory,
        CancellationToken cancellationToken,
        ITraceLogger? logger);

    /// <summary>Whether the current process is elevated (administrator) - required before <see cref="ApplyImage" />.</summary>
    bool IsProcessElevated();

    /// <summary>
    ///     Reads the image-index metadata from <paramref name="wimPath" /> (no elevation needed). Never throws for a bad,
    ///     corrupt, locked, or non-WIM file - it returns <see cref="WimImageListStatus.NotAWim" />.
    /// </summary>
    WimImageList ReadImageList(string wimPath, ITraceLogger? logger);
}
