// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

// ReSharper disable InconsistentNaming
namespace EventLogExpert.Eventing.OfflineImaging.Interop;

internal static class Win32ErrorCodes
{
    internal const int ERROR_DISK_FULL = 112;
    // OpenVirtualDisk returns this for corrupt files and non-ISO inputs.
    internal const int ERROR_FILE_CORRUPT = 1392;
    internal const int ERROR_INVALID_DATA = 13;
    internal const int ERROR_INVALID_PARAMETER = 87;
    internal const int ERROR_NOT_SUPPORTED = 50;
    // WIMApplyImage returns this when its message callback cancels.
    internal const int ERROR_REQUEST_ABORTED = 1235;
    internal const int ERROR_SUCCESS = 0;
}
