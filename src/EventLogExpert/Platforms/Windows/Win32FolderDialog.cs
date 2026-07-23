// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace EventLogExpert.Platforms.Windows;

internal static partial class Win32FolderDialog
{
    private const uint BIF_EDITBOX = 0x00000010;
    private const uint BIF_NEWDIALOGSTYLE = 0x00000040;
    private const uint BIF_RETURNONLYFSDIRS = 0x00000001;
    // MAX_PATH: SHBrowseForFolder writes the selected folder's display NAME (not path) into pszDisplayName and takes no
    // length parameter, assuming a MAX_PATH buffer. A real >= MAX_PATH buffer is unambiguously overrun-safe.
    private const int DisplayNameChars = 260;
    private const int GPFIDL_DEFAULT = 0x00000000;

    // 256 chars bounds the stack allocation against pathologically long external title input.
    private const int MaxTitleChars = 256;

    // 32K char = 64 KB: covers the documented Windows absolute maximum path length, mirroring Win32FileDialog and the
    // native extension's SHGetPathFromIDListEx buffer -- avoids the MAX_PATH ceiling GPFIDL_DEFAULT otherwise escapes.
    private const int PathBufferChars = 32 * 1024;

    /// <summary>
    ///     Presents the folder dialog. Returns the selected folder's file-system path, or <c>null</c> only when the user
    ///     cancelled. Throws <see cref="InvalidOperationException" /> when OLE cannot be initialized or when a selected item
    ///     cannot be resolved to a file-system path (so a genuine failure is never silently reported as a cancel).
    /// </summary>
    public static string? PickFolder(IntPtr hwndOwner, string? title = null)
    {
        // BIF_NEWDIALOGSTYLE requires the calling STA thread to be OLE-initialized. The runtime already CoInitializes
        // the dedicated STA worker as apartment-threaded, so OleInitialize typically returns S_FALSE (COM already
        // initialized) while still layering OLE on top. Throw on any failure (including RPC_E_CHANGED_MODE, which
        // cannot occur on the fresh guaranteed-STA thread) so OleUninitialize is only ever called after a success.
        var oleResult = OleInitialize(IntPtr.Zero);

        if (oleResult < 0)
        {
            throw new InvalidOperationException(
                $"Could not initialize OLE to present the folder dialog (HRESULT 0x{oleResult:X8}).");
        }

        try
        {
            return PickFolderCore(hwndOwner, title);
        }
        finally
        {
            OleUninitialize();
        }
    }

    /// <summary>
    ///     Builds (or measures, when <paramref name="destination" /> is empty) the null-terminated title buffer. Returns
    ///     0 when <paramref name="title" /> is null or empty (the dialog uses no caption prompt in that case -- pass
    ///     IntPtr.Zero for <c>lpszTitle</c>). Titles longer than <see cref="MaxTitleChars" /> are silently truncated to bound
    ///     the stack allocation against external input (mirror of the helper in <see cref="Win32FileDialog" />).
    /// </summary>
    private static int CopyNullableTitle(Span<char> destination, string? title)
    {
        if (string.IsNullOrEmpty(title)) { return 0; }

        var titleLen = Math.Min(title.Length, MaxTitleChars);
        var needed = titleLen + 1;

        if (destination.IsEmpty) { return needed; }

        title.AsSpan(0, titleLen).CopyTo(destination);
        destination[titleLen] = '\0';

        return needed;
    }

    [LibraryImport("ole32.dll")]
    private static partial int OleInitialize(IntPtr pvReserved);

    [LibraryImport("ole32.dll")]
    private static partial void OleUninitialize();

    private static unsafe string? PickFolderCore(IntPtr hwndOwner, string? title)
    {
        Span<char> displayNameBuffer = stackalloc char[DisplayNameChars];
        displayNameBuffer.Clear();

        Span<char> titleBuffer = stackalloc char[CopyNullableTitle(default, title)];
        CopyNullableTitle(titleBuffer, title);

        fixed (char* displayNamePtr = displayNameBuffer)
        {
            fixed (char* titlePtr = titleBuffer)
            {
                var browseInfo = new BrowseInfo
                {
                    hwndOwner = hwndOwner,
                    pidlRoot = IntPtr.Zero,
                    pszDisplayName = (IntPtr)displayNamePtr,
                    lpszTitle = titleBuffer.IsEmpty ? IntPtr.Zero : (IntPtr)titlePtr,
                    ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE | BIF_EDITBOX,
                    lpfn = IntPtr.Zero,
                    lParam = IntPtr.Zero,
                    iImage = 0
                };

                var pidl = SHBrowseForFolder(ref browseInfo);

                if (pidl == IntPtr.Zero) { return null; } // user cancelled

                try
                {
                    Span<char> pathBuffer = stackalloc char[PathBufferChars];
                    pathBuffer.Clear();

                    fixed (char* pathPtr = pathBuffer)
                    {
                        if (!SHGetPathFromIDListEx(pidl, (IntPtr)pathPtr, PathBufferChars, GPFIDL_DEFAULT))
                        {
                            throw new InvalidOperationException(
                                "The selected item is not a file-system folder.");
                        }

                        var path = new string(pathPtr);

                        return string.IsNullOrEmpty(path)
                            ? throw new InvalidOperationException(
                                "The selected folder could not be resolved to a file-system path.")
                            : path;
                    }
                }
                finally
                {
                    // The PIDL is allocated by the shell task allocator (identical to the COM task allocator), so
                    // Marshal.FreeCoTaskMem is the correct release -- matching the native extension's CoTaskMemFree.
                    Marshal.FreeCoTaskMem(pidl);
                }
            }
        }
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHBrowseForFolderW")]
    private static partial IntPtr SHBrowseForFolder(ref BrowseInfo browseInfo);

    [LibraryImport("shell32.dll", EntryPoint = "SHGetPathFromIDListEx")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SHGetPathFromIDListEx(IntPtr pidl, IntPtr pszPath, uint cchPath, int uOpts);

    [StructLayout(LayoutKind.Sequential)]
    private struct BrowseInfo
    {
        public IntPtr hwndOwner;
        public IntPtr pidlRoot;
        public IntPtr pszDisplayName;
        public IntPtr lpszTitle;
        public uint ulFlags;
        public IntPtr lpfn;
        public IntPtr lParam;
        public int iImage;
    }
}
