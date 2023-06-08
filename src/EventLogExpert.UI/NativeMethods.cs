// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.InteropServices;

// ReSharper disable InconsistentNaming
// We are defining some win32 types in this file, so we
// are not following the usual C# naming conventions.

namespace EventLogExpert.UI;

public class NativeMethods
{
    /// <summary>Flags for the RegisterApplicationRestart function</summary>
    [Flags]
    public enum RestartFlags
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

    // https://learn.microsoft.com/en-us/windows/msix/non-store-developer-updates
    /// <summary>Registers the active instance of an application for restart.</summary>
    /// <param name="pwzCommandLine">
    ///     A pointer to a Unicode string that specifies the command-line arguments for the
    ///     application when it is restarted. The maximum size of the command line that you can specify is RESTART_MAX_CMD_LINE
    ///     characters. Do not include the name of the executable in the command line; this function adds it for you. If this
    ///     parameter is NULL or an empty string, the previously registered command line is removed. If the argument contains
    ///     spaces, use quotes around the argument.
    /// </param>
    /// <param name="dwFlags">One of the options specified in RestartFlags</param>
    /// <returns>
    ///     This function returns S_OK on success or one of the following error codes: E_FAIL for internal error.
    ///     E_INVALIDARG if rhe specified command line is too long.
    /// </returns>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterApplicationRestart(string? pwzCommandLine, RestartFlags dwFlags);
}
