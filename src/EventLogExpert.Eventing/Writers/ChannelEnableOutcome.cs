// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Writers;

public enum ChannelEnableOutcome
{
    /// <summary>The channel was disabled and has now been enabled (the save committed).</summary>
    Enabled,

    /// <summary>The channel was already enabled, so no write was performed.</summary>
    AlreadyEnabled,

    /// <summary>The caller is not elevated; the enable was not attempted (set by the runtime service).</summary>
    NotElevated,

    /// <summary>The channel configuration could not be opened or saved because access was denied.</summary>
    AccessDenied,

    /// <summary>No channel with the requested name is registered on the computer.</summary>
    NotFound,

    /// <summary>The enable failed for any other reason; see the Win32 error code.</summary>
    Failed
}
