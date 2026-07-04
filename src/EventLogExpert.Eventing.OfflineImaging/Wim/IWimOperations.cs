// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Eventing.OfflineImaging.Wim;

// Coarse seam keeps WIMGAPI handle and callback lifetime inside WimOperations while tests fake the state machine.
internal interface IWimOperations
{
    // Returns the Win32 error code; 0 is success and ERROR_REQUEST_ABORTED is cancellation.
    int ApplyImage(
        string wimPath,
        int imageIndex,
        string destinationDirectory,
        string scratchDirectory,
        CancellationToken cancellationToken,
        ITraceLogger? logger);

    bool IsProcessElevated();

    // Bad, corrupt, locked, or non-WIM files return NotAWim instead of throwing.
    WimImageList ReadImageList(string wimPath, ITraceLogger? logger);
}
