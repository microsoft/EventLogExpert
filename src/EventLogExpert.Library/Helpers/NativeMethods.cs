// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.InteropServices;

// ReSharper disable InconsistentNaming
// We are defining some win32 types in this file, so we
// are not following the usual C# naming conventions.

namespace EventLogExpert.Library.Helpers;

public class NativeMethods
{
    public const int RT_MESSAGETABLE = 11;

    public delegate bool EnumResTypeProc(IntPtr hModule, string lpszType, IntPtr lParam);

    [Flags]
    public enum LoadLibraryFlags : uint
    {
        None = 0,
        DONT_RESOLVE_DLL_REFERENCES = 0x00000001,
        LOAD_IGNORE_CODE_AUTHZ_LEVEL = 0x00000010,
        LOAD_LIBRARY_AS_DATAFILE = 0x00000002,
        LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE = 0x00000040,
        LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x00000020,
        LOAD_LIBRARY_SEARCH_APPLICATION_DIR = 0x00000200,
        LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000,
        LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR = 0x00000100,
        LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800,
        LOAD_LIBRARY_SEARCH_USER_DIRS = 0x00000400,
        LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008
    }

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

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool EnumResourceTypes(IntPtr hModule, EnumResTypeProc lpEnumFunc, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr FindResource(IntPtr hModule, int lpID, int lpType);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr LoadLibrary(string fileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hReservedNull, LoadLibraryFlags dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr LoadResource(IntPtr hModule, IntPtr hResInfo);

    [DllImport("kernel32.dll")]
    public static extern IntPtr LockResource(IntPtr hResData);

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

    [StructLayout(LayoutKind.Sequential)]
    public struct MESSAGE_RESOURCE_BLOCK
    {
        public int LowId;
        public int HighId;
        public int OffsetToEntries;
    }
}
