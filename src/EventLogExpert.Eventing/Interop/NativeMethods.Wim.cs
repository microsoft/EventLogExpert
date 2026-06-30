// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Interop;

internal static partial class NativeMethods
{
    internal const uint WIM_COMPRESS_NONE = 0;

    /// <summary>
    ///     <see cref="WIMApplyImage" /> flags that skip restoring the captured directory/file security descriptors, so
    ///     the extracted tree is owned by the (elevated) caller and is readable + deletable. Without these the default apply
    ///     restores TrustedInstaller-owned ACLs, which would block staging the hives and the recursive temp cleanup.
    /// </summary>
    internal const uint WIM_FLAG_NO_DIRACL = 0x00000010;

    internal const uint WIM_FLAG_NO_FILEACL = 0x00000020;
    /// <summary>Read access for <see cref="WIMCreateFile" /> (GENERIC_READ).</summary>
    internal const uint WIM_GENERIC_READ = 0x80000000;

    /// <summary><see cref="WIMRegisterMessageCallback" /> sentinel returned when registration fails.</summary>
    internal const uint WIM_INVALID_CALLBACK_VALUE = 0xFFFFFFFF;

    /// <summary>Message callback return: abort the in-progress apply (WIMApplyImage then fails with ERROR_REQUEST_ABORTED).</summary>
    internal const uint WIM_MSG_ABORT_IMAGE = 0xFFFFFFFF;

    /// <summary>Apply/capture progress message id; wParam carries the percent complete (0-100).</summary>
    internal const uint WIM_MSG_PROGRESS = 0x9478;

    /// <summary>Message callback return: continue the operation.</summary>
    internal const uint WIM_MSG_SUCCESS = 0;

    /// <summary>Open-existing disposition for <see cref="WIMCreateFile" /> (OPEN_EXISTING).</summary>
    internal const uint WIM_OPEN_EXISTING = 3;

    private const string WimgapiApi = "wimgapi.dll";

    /// <summary>
    ///     Native WIM message callback (<c>fpMessageProc</c>). Returns <see cref="WIM_MSG_SUCCESS" /> to continue or
    ///     <see cref="WIM_MSG_ABORT_IMAGE" /> to abort the current apply.
    /// </summary>
    internal delegate uint WimMessageCallback(uint messageId, IntPtr wParam, IntPtr lParam, IntPtr userData);

    /// <summary>
    ///     Frees a buffer allocated by the system with <c>LocalAlloc</c> (e.g. the XML from
    ///     <see cref="WIMGetImageInformation" />).
    /// </summary>
    [LibraryImport(Kernel32Api, EntryPoint = "LocalFree", SetLastError = true)]
    internal static partial IntPtr LocalFree(IntPtr mem);

    /// <summary>
    ///     Applies (extracts) the loaded <paramref name="image" /> to <paramref name="path" /> with
    ///     <paramref name="applyFlags" />. Requires administrator privileges. Returns <see langword="false" /> on failure
    ///     (sets last error); a callback-driven abort surfaces as <c>ERROR_REQUEST_ABORTED</c>.
    /// </summary>
    [LibraryImport(WimgapiApi, EntryPoint = "WIMApplyImage", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WIMApplyImage(WimImageSafeHandle image, string path, uint applyFlags);

    /// <summary>Closes a WIM file or image handle. Used by the <c>SafeHandle</c> wrappers' release.</summary>
    [LibraryImport(WimgapiApi, EntryPoint = "WIMCloseHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WIMCloseHandle(IntPtr handle);

    /// <summary>
    ///     Opens a <c>.wim</c>/<c>.esd</c> image file. Returns an invalid handle on failure (sets last error);
    ///     <paramref name="creationResult" /> receives whether the file was created or opened.
    /// </summary>
    [LibraryImport(WimgapiApi, EntryPoint = "WIMCreateFile", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial WimFileSafeHandle WIMCreateFile(
        string wimPath,
        uint desiredAccess,
        uint creationDisposition,
        uint flagsAndAttributes,
        uint compressionType,
        out uint creationResult);

    /// <summary>Returns the number of images in <paramref name="wim" /> (0 on failure, sets last error).</summary>
    [LibraryImport(WimgapiApi, EntryPoint = "WIMGetImageCount", SetLastError = true)]
    internal static partial uint WIMGetImageCount(WimFileSafeHandle wim);

    /// <summary>
    ///     Returns the WIM's <c>&lt;WIM&gt;</c> XML metadata (UTF-16) describing every image, in a system-allocated
    ///     buffer the caller MUST release with <see cref="LocalFree" />. <paramref name="imageInfoBytes" /> is the byte
    ///     length.
    /// </summary>
    [LibraryImport(WimgapiApi, EntryPoint = "WIMGetImageInformation", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WIMGetImageInformation(WimFileSafeHandle wim, out IntPtr imageInfo, out uint imageInfoBytes);

    /// <summary>
    ///     Loads the 1-based image <paramref name="imageIndex" /> from <paramref name="wim" />. Invalid handle on
    ///     failure.
    /// </summary>
    [LibraryImport(WimgapiApi, EntryPoint = "WIMLoadImage", SetLastError = true)]
    internal static partial WimImageSafeHandle WIMLoadImage(WimFileSafeHandle wim, uint imageIndex);

    /// <summary>
    ///     Registers a per-WIM message callback (a raw function pointer, so the caller MUST keep the delegate rooted for
    ///     the call's duration). Returns the callback index, or <see cref="WIM_INVALID_CALLBACK_VALUE" /> on failure.
    /// </summary>
    [LibraryImport(WimgapiApi, EntryPoint = "WIMRegisterMessageCallback", SetLastError = true)]
    internal static partial uint WIMRegisterMessageCallback(WimFileSafeHandle wim, IntPtr messageProc, IntPtr userData);

    /// <summary>Sets the scratch directory WIMGAPI uses while loading/applying images on <paramref name="wim" />.</summary>
    [LibraryImport(WimgapiApi, EntryPoint = "WIMSetTemporaryPath", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WIMSetTemporaryPath(WimFileSafeHandle wim, string path);

    /// <summary>Unregisters the callback previously registered for <paramref name="wim" />.</summary>
    [LibraryImport(WimgapiApi, EntryPoint = "WIMUnregisterMessageCallback", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WIMUnregisterMessageCallback(WimFileSafeHandle wim, IntPtr messageProc);
}
