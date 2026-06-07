// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

// ReSharper disable InconsistentNaming
// We are defining some win32 types in this file, so we
// are not following the usual C# naming conventions.

namespace EventLogExpert.WindowsPlatform.Restart;

/// <summary>Flags for the RegisterApplicationRestart function</summary>
[Flags]
internal enum RestartFlags
{
    /// <summary>None of the options below.</summary>
    NONE = 0,

    /// <summary>Do not restart the process if it terminates due to an unhandled exception.</summary>
    RESTART_NO_CRASH = 1,
    /// <summary>Do not restart the process if it terminates due to the application not responding.</summary>
    RESTART_NO_HANG = 2,
    /// <summary>Do not restart the process if it terminates due to the installation of an update.</summary>
    RESTART_NO_PATCH = 4,
    /// <summary>Do not restart the process if the computer is restarted as the result of an update.</summary>
    RESTART_NO_REBOOT = 8
}
