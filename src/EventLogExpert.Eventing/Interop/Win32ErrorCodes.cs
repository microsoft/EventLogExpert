// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

// ReSharper disable InconsistentNaming
namespace EventLogExpert.Eventing.Interop;

internal static class Win32ErrorCodes
{
    internal const int ERROR_ACCESS_DENIED = 5;
    /// <summary>The registry hive needs log recovery (a dirty hive captured from a live/imaged system).</summary>
    internal const int ERROR_BADDB = 1009;
    internal const int ERROR_CANCELLED = 1223;
    /// <summary>The destination drive ran out of space (e.g. during <c>WIMApplyImage</c>).</summary>
    internal const int ERROR_DISK_FULL = 112;
    /// <summary>The file is not a recognized virtual-disk image (a corrupt or non-ISO file passed to OpenVirtualDisk).</summary>
    internal const int ERROR_FILE_CORRUPT = 1392;
    internal const int ERROR_FILE_NOT_FOUND = 2;
    internal const int ERROR_INSUFFICIENT_BUFFER = 122;
    internal const int ERROR_INVALID_DATA = 13;
    internal const int ERROR_INVALID_HANDLE = 6;
    internal const int ERROR_INVALID_PARAMETER = 87;
    internal const int ERROR_NO_MORE_ITEMS = 259;
    /// <summary>A token privilege requested by <c>AdjustTokenPrivileges</c> is not held by the token.</summary>
    internal const int ERROR_NOT_ALL_ASSIGNED = 1300;
    internal const int ERROR_NOT_SUPPORTED = 50;
    internal const int ERROR_PATH_NOT_FOUND = 3;
    /// <summary>A required privilege (e.g. <c>SeRestorePrivilege</c>) is not held by the caller.</summary>
    internal const int ERROR_PRIVILEGE_NOT_HELD = 1314;
    /// <summary>The registry hive is corrupt; like <see cref="ERROR_BADDB" /> it marks a hive that needs recovery.</summary>
    internal const int ERROR_REGISTRY_CORRUPT = 1015;
    /// <summary>An operation was aborted via its message callback - <c>WIMApplyImage</c> returns this when cancelled.</summary>
    internal const int ERROR_REQUEST_ABORTED = 1235;
    internal const int ERROR_SUCCESS = 0;

    internal const int ERROR_EVT_CHANNEL_NOT_FOUND = 0x3A9F;
    internal const int ERROR_EVT_INVALID_EVENT_DATA = 0x3A9D;
    internal const int ERROR_EVT_MAX_INSERTS_REACHED = 0x3AB7;
    internal const int ERROR_EVT_MESSAGE_ID_NOT_FOUND = 0x3AB4;
    internal const int ERROR_EVT_MESSAGE_NOT_FOUND = 0x3AB3;
    internal const int ERROR_EVT_PUBLISHER_METADATA_NOT_FOUND = 0x3A9A;
    internal const int ERROR_EVT_UNRESOLVED_PARAMETER_INSERT = 0x3AB6;
    internal const int ERROR_EVT_UNRESOLVED_VALUE_INSERT = 0x3AB5;

    internal const int RPC_S_CALL_CANCELED = 1818;
}
