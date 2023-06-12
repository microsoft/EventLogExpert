// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.InteropServices;

// ReSharper disable InconsistentNaming
// We are defining some win32 types in this file, so we
// are not following the usual C# naming conventions.

namespace EventLogExpert.Eventing.Helpers;

public class NativeMethods
{
    public const int RT_MESSAGETABLE = 11;

    public delegate bool EnumResTypeProc(nint hModule, string lpszType, nint lParam);

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

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool EnumResourceTypes(nint hModule, EnumResTypeProc lpEnumFunc, nint lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint FindResource(nint hModule, int lpID, int lpType);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool FreeLibrary(nint hModule);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint LoadLibrary(string fileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint LoadLibraryEx(string lpFileName, nint hReservedNull, LoadLibraryFlags dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint LoadResource(nint hModule, nint hResInfo);

    [DllImport("kernel32.dll")]
    public static extern nint LockResource(nint hResData);

    [StructLayout(LayoutKind.Sequential)]
    public struct MESSAGE_RESOURCE_BLOCK
    {
        public int LowId;
        public int HighId;
        public int OffsetToEntries;
    }
}
