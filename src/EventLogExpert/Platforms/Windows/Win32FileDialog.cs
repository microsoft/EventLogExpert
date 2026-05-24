// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace EventLogExpert.Platforms.Windows;

/// <summary>
///     Win32 file-open dialog via the procedural <c>comdlg32!GetOpenFileNameW</c> API. We deliberately do NOT use WinUI's
///     <c>FileOpenPicker</c> nor the shell <c>IFileOpenDialog</c> COM class — both fail under elevated MAUI WinUI
///     processes:
///     <list type="bullet">
///         <item>
///             <c>FileOpenPicker</c> throws <c>0x80004005</c> even with <c>InitializeWithWindow</c> applied (broker
///             activation path is unavailable to elevated callers).
///         </item>
///         <item>
///             <c>IFileOpenDialog</c> throws <c>REGDB_E_CLASSNOTREG</c> from a fresh STA thread because MSIX registry
///             virtualization hides system COM classes that aren't declared in the package manifest.
///         </item>
///     </list>
///     <c>GetOpenFileNameW</c> uses no COM activation and works in every context — including elevated MSIX apps,
///     unpackaged elevated apps, and standard user mode. Buffers are stack-allocated to match the codebase's
///     <c>stackalloc</c>-based P/Invoke style (see <c>NativeMethods.FormatMessageW</c> in EventLogExpert.Eventing).
/// </summary>
internal static partial class Win32FileDialog
{
    // Sized to comfortably fit a multi-select buffer (one directory path + many file names). 32K chars = 64 KB on
    // the stack — well under the default 1 MB stack limit, but big enough that we won't truncate in practice.
    private const int FileBufferChars = 32 * 1024;

    // Dialog titles are short by convention (Windows shell never sets one longer than ~80 chars). 256 chars is
    // generous and bounds the stack allocation against pathologically long external input.
    private const int MaxTitleChars = 256;
    private const int OFN_ALLOWMULTISELECT = 0x00000200;
    private const int OFN_DONTADDTORECENT = 0x02000000;
    private const int OFN_EXPLORER = 0x00080000;
    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_HIDEREADONLY = 0x00000004;
    private const int OFN_NOCHANGEDIR = 0x00000008;
    private const int OFN_NOREADONLYRETURN = 0x00008000;
    private const int OFN_OVERWRITEPROMPT = 0x00000002;
    private const int OFN_PATHMUSTEXIST = 0x00000800;

    /// <summary>Returns the picked paths (empty if the user cancelled).</summary>
    public static unsafe IReadOnlyList<string> PickMultipleFiles(
        IntPtr hwndOwner,
        IReadOnlyList<string> extensions,
        string? title = null)
    {
        ArgumentNullException.ThrowIfNull(extensions);

        Span<char> fileBuffer = stackalloc char[FileBufferChars];
        fileBuffer.Clear();

        Span<char> filter = stackalloc char[BuildFilter(default, extensions)];
        BuildFilter(filter, extensions);

        Span<char> titleBuffer = stackalloc char[CopyNullableTitle(default, title)];
        CopyNullableTitle(titleBuffer, title);

        fixed (char* fileBufferPtr = fileBuffer)
        fixed (char* filterPtr = filter)
        fixed (char* titlePtr = titleBuffer)
        {
            var ofn = BuildOfn(
                hwndOwner,
                (IntPtr)filterPtr,
                (IntPtr)fileBufferPtr,
                titleBuffer.IsEmpty ? IntPtr.Zero : (IntPtr)titlePtr,
                multiSelect: true);

            if (!GetOpenFileNameW(ref ofn))
            {
                ThrowIfDialogError();
                return [];
            }

            return ParseMultiSelectBuffer(fileBuffer);
        }
    }

    /// <summary>Picks a destination for save (returns the path with extension auto-appended; <c>null</c> if cancelled).</summary>
    public static unsafe string? PickSaveFile(
        IntPtr hwndOwner,
        IReadOnlyList<string> extensions,
        string? suggestedFileName = null,
        string? title = null)
    {
        ArgumentNullException.ThrowIfNull(extensions);

        if (extensions.Count == 0)
        {
            throw new ArgumentException("At least one extension is required.", nameof(extensions));
        }

        if (string.IsNullOrWhiteSpace(extensions[0]))
        {
            throw new ArgumentException("The first extension cannot be null or whitespace.", nameof(extensions));
        }

        Span<char> fileBuffer = stackalloc char[FileBufferChars];
        fileBuffer.Clear();
        if (!string.IsNullOrEmpty(suggestedFileName))
        {
            // Leave room for the null terminator; truncate if the caller passed an absurdly long suggestion
            // (FileBufferChars is 32K; typical suggestions are well under 256, but external input is external input).
            var copyLen = Math.Min(suggestedFileName.Length, FileBufferChars - 1);
            suggestedFileName.AsSpan(0, copyLen).CopyTo(fileBuffer);
        }

        Span<char> filter = stackalloc char[BuildFilter(default, extensions)];
        BuildFilter(filter, extensions);

        // GetSaveFileNameW auto-appends lpstrDefExt when the user types a filename without an extension.
        // We strip the leading "." since the API expects just the extension chars.
        var defaultExt = extensions[0].TrimStart('.');
        Span<char> defaultExtBuffer = stackalloc char[defaultExt.Length + 1];
        defaultExt.AsSpan().CopyTo(defaultExtBuffer);
        defaultExtBuffer[defaultExt.Length] = '\0';

        Span<char> titleBuffer = stackalloc char[CopyNullableTitle(default, title)];
        CopyNullableTitle(titleBuffer, title);

        fixed (char* fileBufferPtr = fileBuffer)
        fixed (char* filterPtr = filter)
        fixed (char* defaultExtPtr = defaultExtBuffer)
        fixed (char* titlePtr = titleBuffer)
        {
            var ofn = new OpenFileName
            {
                lStructSize = OpenFileName.NativeSize,
                hwndOwner = hwndOwner,
                lpstrFilter = (IntPtr)filterPtr,
                nFilterIndex = 1,
                lpstrFile = (IntPtr)fileBufferPtr,
                nMaxFile = FileBufferChars,
                lpstrDefExt = (IntPtr)defaultExtPtr,
                lpstrTitle = titleBuffer.IsEmpty ? IntPtr.Zero : (IntPtr)titlePtr,
                Flags = OFN_EXPLORER
                    | OFN_PATHMUSTEXIST
                    | OFN_OVERWRITEPROMPT
                    | OFN_HIDEREADONLY
                    | OFN_NOREADONLYRETURN
                    | OFN_NOCHANGEDIR
                    | OFN_DONTADDTORECENT
            };

            if (!GetSaveFileNameW(ref ofn))
            {
                ThrowIfDialogError();
                return null;
            }

            var path = new string(fileBufferPtr);
            return string.IsNullOrEmpty(path) ? null : path;
        }
    }

    /// <summary>Returns the picked path or <c>null</c> if the user cancelled.</summary>
    public static unsafe string? PickSingleFile(
        IntPtr hwndOwner,
        IReadOnlyList<string> extensions,
        string? title = null)
    {
        ArgumentNullException.ThrowIfNull(extensions);

        Span<char> fileBuffer = stackalloc char[FileBufferChars];
        fileBuffer.Clear();

        Span<char> filter = stackalloc char[BuildFilter(default, extensions)];
        BuildFilter(filter, extensions);

        Span<char> titleBuffer = stackalloc char[CopyNullableTitle(default, title)];
        CopyNullableTitle(titleBuffer, title);

        fixed (char* fileBufferPtr = fileBuffer)
        fixed (char* filterPtr = filter)
        fixed (char* titlePtr = titleBuffer)
        {
            var ofn = BuildOfn(
                hwndOwner,
                (IntPtr)filterPtr,
                (IntPtr)fileBufferPtr,
                titleBuffer.IsEmpty ? IntPtr.Zero : (IntPtr)titlePtr,
                multiSelect: false);

            if (!GetOpenFileNameW(ref ofn))
            {
                ThrowIfDialogError();
                return null;
            }

            var path = new string(fileBufferPtr);
            return string.IsNullOrEmpty(path) ? null : path;
        }
    }

    /// <summary>
    ///     Builds (or measures, when <paramref name="destination" /> is empty) the OPENFILENAME filter buffer in the
    ///     <c>"label\0pattern\0...\0\0"</c> null-separated null-terminated format. Returns the total number of chars written
    ///     or needed.
    /// </summary>
    private static int BuildFilter(Span<char> destination, IReadOnlyList<string> extensions)
    {
        var pattern = string.Join(";", extensions.Select(e => "*" + e));
        var label = $"Supported types ({pattern})";

        // label\0pattern\0All files\0*.*\0\0
        var needed = label.Length + 1 + pattern.Length + 1 + "All files".Length + 1 + "*.*".Length + 1 + 1;

        if (destination.IsEmpty) { return needed; }

        var position = 0;
        position += CopyWithNullTerminator(label, destination[position..]);
        position += CopyWithNullTerminator(pattern, destination[position..]);
        position += CopyWithNullTerminator("All files", destination[position..]);
        position += CopyWithNullTerminator("*.*", destination[position..]);
        destination[position] = '\0'; // double-null terminator

        return needed;
    }

    private static OpenFileName BuildOfn(IntPtr hwndOwner, IntPtr filterPtr, IntPtr fileBufferPtr, IntPtr titlePtr, bool multiSelect) =>
        new()
        {
            lStructSize = OpenFileName.NativeSize,
            hwndOwner = hwndOwner,
            lpstrFilter = filterPtr,
            nFilterIndex = 1,
            lpstrFile = fileBufferPtr,
            nMaxFile = FileBufferChars,
            lpstrTitle = titlePtr,
            Flags = OFN_EXPLORER
                    | OFN_PATHMUSTEXIST
                    | OFN_FILEMUSTEXIST
                    | OFN_HIDEREADONLY
                    | OFN_NOCHANGEDIR
                    | OFN_DONTADDTORECENT
                    | (multiSelect ? OFN_ALLOWMULTISELECT : 0)
        };

    /// <summary>
    ///     Builds (or measures, when <paramref name="destination" /> is empty) the null-terminated title buffer. Returns
    ///     0 when <paramref name="title" /> is null or empty (the dialog uses its default title in that case — pass
    ///     IntPtr.Zero for <c>lpstrTitle</c>). Otherwise returns the buffer size needed (clamped title length + 1 for
    ///     the null terminator). Titles longer than <see cref="MaxTitleChars" /> are silently truncated to bound the
    ///     stack allocation against external input (mirror of the suggested-filename guard in <see cref="PickSaveFile" />).
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

    [LibraryImport("Comdlg32.dll", EntryPoint = "CommDlgExtendedError")]
    private static partial int CommDlgExtendedError();

    private static int CopyWithNullTerminator(ReadOnlySpan<char> source, Span<char> destination)
    {
        source.CopyTo(destination);
        destination[source.Length] = '\0';
        return source.Length + 1;
    }

    [LibraryImport("Comdlg32.dll", EntryPoint = "GetOpenFileNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetOpenFileNameW(ref OpenFileName ofn);

    [LibraryImport("Comdlg32.dll", EntryPoint = "GetSaveFileNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSaveFileNameW(ref OpenFileName ofn);

    /// <summary>
    ///     With <c>OFN_ALLOWMULTISELECT</c> + <c>OFN_EXPLORER</c>, the file buffer contains the directory path followed
    ///     by null, then each filename followed by null, terminated by a double-null. A single selection just contains the
    ///     full path followed by null-null. Both shapes are normalised to a list of absolute paths.
    /// </summary>
    private static IReadOnlyList<string> ParseMultiSelectBuffer(ReadOnlySpan<char> buffer)
    {
        if (buffer.IsEmpty || buffer[0] == '\0') { return []; }

        var parts = new List<string>();
        var start = 0;

        for (var i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] != '\0') { continue; }

            if (i == start) { break; } // double-null terminator

            parts.Add(new string(buffer[start..i]));
            start = i + 1;
        }

        return parts.Count switch
        {
            // Single selection: the only entry is the full path.
            1 => [parts[0]],
            // Multi-select: first entry is the directory, remaining entries are filenames inside it.
            > 1 => parts.Skip(1).Select(name => Path.Combine(parts[0], name)).ToArray(),
            _ => []
        };
    }

    private static void ThrowIfDialogError()
    {
        var code = CommDlgExtendedError();
        if (code == 0) { return; } // 0 = user cancelled, not an error

        throw new InvalidOperationException(
            $"Win32 file dialog failed with CDERR code 0x{code:X4} (see CommDlgExtendedError docs).");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenFileName
    {
        public static readonly int NativeSize = Marshal.SizeOf<OpenFileName>();

        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public IntPtr lpstrFilter;
        public IntPtr lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public IntPtr lpstrFileTitle;
        public int nMaxFileTitle;
        public IntPtr lpstrInitialDir;
        public IntPtr lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public IntPtr lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public IntPtr lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }
}
