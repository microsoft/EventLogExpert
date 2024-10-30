// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

// ReSharper disable InconsistentNaming
namespace EventLogExpert.Eventing.Helpers;

internal static class Interop
{
    internal const int ERROR_FILE_NOT_FOUND = 2;
    internal const int ERROR_PATH_NOT_FOUND = 3;
    internal const int ERROR_ACCESS_DENIED = 5;
    internal const int ERROR_INVALID_HANDLE = 6;
    internal const int ERROR_INVALID_DATA = 13;
    
    internal const int ERROR_INSUFFICIENT_BUFFER = 122;
    internal const int ERROR_NO_MORE_ITEMS = 259;

    internal const int RPC_S_CALL_CANCELED = 1818;
    internal const int ERROR_CANCELLED = 1223;
    
    internal const int ERROR_EVT_PUBLISHER_METADATA_NOT_FOUND = 0x3A9A;
    internal const int ERROR_EVT_INVALID_EVENT_DATA = 0x3A9D;
    internal const int ERROR_EVT_CHANNEL_NOT_FOUND = 0x3A9F;

    internal const int ERROR_EVT_MESSAGE_NOT_FOUND = 0x3AB3;
    internal const int ERROR_EVT_MESSAGE_ID_NOT_FOUND = 0x3AB4;
    internal const int ERROR_EVT_UNRESOLVED_VALUE_INSERT = 0x3AB5;
    internal const int ERROR_EVT_UNRESOLVED_PARAMETER_INSERT = 0x3AB6;
    internal const int ERROR_EVT_MAX_INSERTS_REACHED = 0x3AB7;
}
