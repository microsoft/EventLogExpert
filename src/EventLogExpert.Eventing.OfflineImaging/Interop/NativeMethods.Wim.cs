// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.OfflineImaging.Interop;

internal static partial class NativeMethods
{
    internal const uint WIM_COMPRESS_NONE = 0;
    // Skip restored ACLs so extracted hives can be staged and temp cleanup can delete the tree.
    internal const uint WIM_FLAG_NO_DIRACL = 0x00000010;
    internal const uint WIM_FLAG_NO_FILEACL = 0x00000020;
    internal const uint WIM_GENERIC_READ = 0x80000000;
    internal const uint WIM_INVALID_CALLBACK_VALUE = 0xFFFFFFFF;
    internal const uint WIM_MSG_ABORT_IMAGE = 0xFFFFFFFF;
    internal const uint WIM_MSG_PROGRESS = 0x9478;
    internal const uint WIM_MSG_SUCCESS = 0;
    internal const uint WIM_OPEN_EXISTING = 3;

    private const string WimgapiApi = "wimgapi.dll";

    internal delegate uint WimMessageCallback(uint messageId, IntPtr wParam, IntPtr lParam, IntPtr userData);

    [LibraryImport(Kernel32Api, EntryPoint = "LocalFree", SetLastError = true)]
    internal static partial IntPtr LocalFree(IntPtr mem);

    [LibraryImport(WimgapiApi, EntryPoint = "WIMApplyImage", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WIMApplyImage(WimImageSafeHandle image, string path, uint applyFlags);

    [LibraryImport(WimgapiApi, EntryPoint = "WIMCloseHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WIMCloseHandle(IntPtr handle);

    [LibraryImport(WimgapiApi, EntryPoint = "WIMCreateFile", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial WimFileSafeHandle WIMCreateFile(
        string wimPath,
        uint desiredAccess,
        uint creationDisposition,
        uint flagsAndAttributes,
        uint compressionType,
        out uint creationResult);

    [LibraryImport(WimgapiApi, EntryPoint = "WIMGetImageCount", SetLastError = true)]
    internal static partial uint WIMGetImageCount(WimFileSafeHandle wim);

    // Caller must release WIMGetImageInformation's system-allocated UTF-16 XML buffer with LocalFree.
    [LibraryImport(WimgapiApi, EntryPoint = "WIMGetImageInformation", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WIMGetImageInformation(WimFileSafeHandle wim, out IntPtr imageInfo, out uint imageInfoBytes);

    [LibraryImport(WimgapiApi, EntryPoint = "WIMLoadImage", SetLastError = true)]
    internal static partial WimImageSafeHandle WIMLoadImage(WimFileSafeHandle wim, uint imageIndex);

    // Caller must root the delegate for the registered callback's duration.
    [LibraryImport(WimgapiApi, EntryPoint = "WIMRegisterMessageCallback", SetLastError = true)]
    internal static partial uint WIMRegisterMessageCallback(WimFileSafeHandle wim, IntPtr messageProc, IntPtr userData);

    [LibraryImport(WimgapiApi, EntryPoint = "WIMSetTemporaryPath", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WIMSetTemporaryPath(WimFileSafeHandle wim, string path);

    [LibraryImport(WimgapiApi, EntryPoint = "WIMUnregisterMessageCallback", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WIMUnregisterMessageCallback(WimFileSafeHandle wim, IntPtr messageProc);
}
