// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.InteropServices;

// ReSharper disable InconsistentNaming
// We are defining some win32 types in this file, so we
// are not following the usual C# naming conventions.

namespace EventLogExpert.Interop;

internal static partial class NativeMethods
{
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
    [LibraryImport("kernel32.dll")]
    internal static partial uint RegisterApplicationRestart(
        [MarshalAs(UnmanagedType.LPWStr)] string? pwzCommandLine,
        RestartFlags dwFlags);
}
