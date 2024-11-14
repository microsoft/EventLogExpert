// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using System.Runtime.InteropServices;

// ReSharper disable InconsistentNaming
// We are defining some win32 types in this file, so we
// are not following the usual C# naming conventions.

namespace EventLogExpert.Eventing.Helpers;

[Flags]
internal enum LoadLibraryFlags : uint
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

internal sealed partial class NativeMethods
{
    internal const int RT_MESSAGETABLE = 11;

    private const string Kernel32Api = "kernel32.dll";

    [LibraryImport(Kernel32Api, SetLastError = true)]
    internal static partial LibraryHandle FindResourceExA(
        LibraryHandle hModule,
        int lpType,
        int lpName,
        ushort wLanguage = 0);

    [LibraryImport(Kernel32Api, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool FreeLibrary(IntPtr hModule);

    [LibraryImport(Kernel32Api, SetLastError = true)]
    internal static partial LibraryHandle LoadLibraryExW(
        [MarshalAs(UnmanagedType.LPWStr)] string lpFileName,
        IntPtr hReservedNull,
        LoadLibraryFlags dwFlags);

    [LibraryImport(Kernel32Api, SetLastError = true)]
    internal static partial IntPtr LoadResource(LibraryHandle hModule, LibraryHandle hResInfo);

    [LibraryImport(Kernel32Api)]
    internal static partial IntPtr LockResource(IntPtr hResData);
}
