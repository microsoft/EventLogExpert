// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Globalization;
using System.Runtime.InteropServices;

namespace EventLogExpert.WindowsPlatform.Dialogs;

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
/// <remarks>
///     Lives in EventLogExpert.WindowsPlatform (not the MAUI head) so the pure filter-buffer construction (
///     <see cref="BuildFilter" />) is unit-testable from EventLogExpert.Windows.Tests. The MAUI head's
///     <c>Win32FileDialogService</c> owns the window handle and STA pump and calls into the public <c>Pick*</c> entries.
/// </remarks>
public static partial class Win32FileDialog
{
    // Bounds the stack-allocated filter buffer against pseudo-localized label growth, mirroring the MaxTitleChars guard.
    internal const int MaxFilterLabelChars = 256;

    // 32K char = 64 KB stack: fits typical multi-select (dir + filenames), well under 1 MB stack.
    private const int FileBufferChars = 32 * 1024;
    // 256 chars bounds stack alloc against pathologically long external title input.
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

    /// <summary>
    ///     Composes the localized <see cref="FilterChrome" /> from already-resolved localized strings. Call this on the
    ///     UI thread (where the localizer resolved the strings) so the "Supported types" label is formatted with the caller's
    ///     culture BEFORE it crosses into the native P/Invoke layer. The extension pattern is snapshotted ONCE and stored on
    ///     the result, so the display label and native token never drift and the buffer passes stay identical. A malformed
    ///     translation format (unbalanced brace, bad index) or a blank/whitespace translation falls back to the English label
    ///     so the picker still opens with usable filter rows; both labels are clamped to <see cref="MaxFilterLabelChars" /> so
    ///     the stack-allocated filter buffer stays bounded against pseudo-loc growth.
    /// </summary>
    public static FilterChrome CreateFilterChrome(
        string supportedTypesFormat,
        string allFilesLabel,
        IReadOnlyList<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(supportedTypesFormat);
        ArgumentNullException.ThrowIfNull(allFilesLabel);
        ArgumentNullException.ThrowIfNull(extensions);

        var pattern = BuildExtensionPattern(extensions);

        string label;

        try
        {
            label = string.Format(CultureInfo.CurrentCulture, supportedTypesFormat, pattern);
        }
        catch (FormatException)
        {
            label = $"Supported types ({pattern})";
        }

        // A blank translation would collapse to a leading "\0" and make GetOpenFileNameW drop the filter row, so fall
        // back to English for empty/whitespace labels as well.
        if (string.IsNullOrWhiteSpace(label)) { label = $"Supported types ({pattern})"; }

        var allFiles = string.IsNullOrWhiteSpace(allFilesLabel) ? "All files" : allFilesLabel;

        return new FilterChrome(Clamp(label), Clamp(allFiles), pattern);
    }

    /// <summary>Returns the picked paths (empty if the user cancelled).</summary>
    public static unsafe IReadOnlyList<string> PickMultipleFiles(
        IntPtr hwndOwner,
        IReadOnlyList<string> extensions,
        FilterChrome chrome,
        string? title = null,
        string? initialDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(extensions);

        Span<char> fileBuffer = stackalloc char[FileBufferChars];
        fileBuffer.Clear();

        Span<char> filter = stackalloc char[BuildFilter(default, chrome)];
        BuildFilter(filter, chrome);

        Span<char> titleBuffer = stackalloc char[CopyNullableTitle(default, title)];
        CopyNullableTitle(titleBuffer, title);

        Span<char> initialDirectoryBuffer = stackalloc char[CopyNullableInitialDirectory(default, initialDirectory)];
        CopyNullableInitialDirectory(initialDirectoryBuffer, initialDirectory);

        fixed (char* fileBufferPtr = fileBuffer)
        {
            fixed (char* filterPtr = filter)
            {
                fixed (char* titlePtr = titleBuffer)
                {
                    fixed (char* initialDirectoryPtr = initialDirectoryBuffer)
                    {
                        var ofn = BuildOfn(
                            hwndOwner,
                            (IntPtr)filterPtr,
                            (IntPtr)fileBufferPtr,
                            titleBuffer.IsEmpty ? IntPtr.Zero : (IntPtr)titlePtr,
                            initialDirectoryBuffer.IsEmpty ? IntPtr.Zero : (IntPtr)initialDirectoryPtr,
                            multiSelect: true);

                        if (!GetOpenFileNameW(ref ofn))
                        {
                            ThrowIfDialogError();

                            return [];
                        }

                        return ParseMultiSelectBuffer(fileBuffer);
                    }
                }
            }
        }
    }

    /// <summary>Picks a destination for save (returns the path with extension auto-appended; <c>null</c> if cancelled).</summary>
    public static unsafe string? PickSaveFile(
        IntPtr hwndOwner,
        IReadOnlyList<string> extensions,
        FilterChrome chrome,
        string? suggestedFileName = null,
        string? title = null,
        string? initialDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(extensions);

        Span<char> fileBuffer = stackalloc char[FileBufferChars];
        fileBuffer.Clear();
        if (!string.IsNullOrEmpty(suggestedFileName))
        {
            // Truncate to FileBufferChars-1 to leave room for null terminator on external-input filenames.
            var copyLen = Math.Min(suggestedFileName.Length, FileBufferChars - 1);
            suggestedFileName.AsSpan(0, copyLen).CopyTo(fileBuffer);
        }

        Span<char> filter = stackalloc char[BuildFilter(default, chrome)];
        BuildFilter(filter, chrome);

        // lpstrDefExt is auto-appended on no-extension input; strip leading dot per Win32 contract. This public entry
        // stays crash-safe for a direct caller that passes an empty list (the MAUI adapter validates non-empty first).
        var defaultExt = extensions.Count > 0 ? extensions[0].TrimStart('.') : string.Empty;
        Span<char> defaultExtBuffer = stackalloc char[defaultExt.Length + 1];
        defaultExt.AsSpan().CopyTo(defaultExtBuffer);
        defaultExtBuffer[defaultExt.Length] = '\0';

        Span<char> titleBuffer = stackalloc char[CopyNullableTitle(default, title)];
        CopyNullableTitle(titleBuffer, title);

        Span<char> initialDirectoryBuffer = stackalloc char[CopyNullableInitialDirectory(default, initialDirectory)];
        CopyNullableInitialDirectory(initialDirectoryBuffer, initialDirectory);

        fixed (char* fileBufferPtr = fileBuffer)
        {
            fixed (char* filterPtr = filter)
            {
                fixed (char* defaultExtPtr = defaultExtBuffer)
                {
                    fixed (char* titlePtr = titleBuffer)
                    {
                        fixed (char* initialDirectoryPtr = initialDirectoryBuffer)
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
                                lpstrInitialDir = initialDirectoryBuffer.IsEmpty ? IntPtr.Zero : (IntPtr)initialDirectoryPtr,
                                lpstrTitle = titleBuffer.IsEmpty ? IntPtr.Zero : (IntPtr)titlePtr,
                                Flags = OFN_EXPLORER |
                                    OFN_PATHMUSTEXIST |
                                    OFN_OVERWRITEPROMPT |
                                    OFN_HIDEREADONLY |
                                    OFN_NOREADONLYRETURN |
                                    OFN_NOCHANGEDIR |
                                    OFN_DONTADDTORECENT
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
                }
            }
        }
    }

    /// <summary>Returns the picked path or <c>null</c> if the user cancelled.</summary>
    public static unsafe string? PickSingleFile(
        IntPtr hwndOwner,
        IReadOnlyList<string> extensions,
        FilterChrome chrome,
        string? title = null,
        string? initialDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(extensions);

        Span<char> fileBuffer = stackalloc char[FileBufferChars];
        fileBuffer.Clear();

        Span<char> filter = stackalloc char[BuildFilter(default, chrome)];
        BuildFilter(filter, chrome);

        Span<char> titleBuffer = stackalloc char[CopyNullableTitle(default, title)];
        CopyNullableTitle(titleBuffer, title);

        Span<char> initialDirectoryBuffer = stackalloc char[CopyNullableInitialDirectory(default, initialDirectory)];
        CopyNullableInitialDirectory(initialDirectoryBuffer, initialDirectory);

        fixed (char* fileBufferPtr = fileBuffer)
        {
            fixed (char* filterPtr = filter)
            {
                fixed (char* titlePtr = titleBuffer)
                {
                    fixed (char* initialDirectoryPtr = initialDirectoryBuffer)
                    {
                        var ofn = BuildOfn(
                            hwndOwner,
                            (IntPtr)filterPtr,
                            (IntPtr)fileBufferPtr,
                            titleBuffer.IsEmpty ? IntPtr.Zero : (IntPtr)titlePtr,
                            initialDirectoryBuffer.IsEmpty ? IntPtr.Zero : (IntPtr)initialDirectoryPtr,
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
            }
        }
    }

    /// <summary>
    ///     Builds the <c>"*.ext;*.ext2"</c> pattern token from the extension list. Called once per picker operation (by
    ///     <see cref="CreateFilterChrome" />) so the display label and the native filter token share one snapshot.
    /// </summary>
    internal static string BuildExtensionPattern(IReadOnlyList<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);

        return string.Join(";", extensions.Select(extension => "*" + extension));
    }

    /// <summary>
    ///     Builds (or measures, when <paramref name="destination" /> is empty) the OPENFILENAME filter buffer in the
    ///     <c>"label\0pattern\0...\0\0"</c> null-separated null-terminated format. Returns the total number of chars written
    ///     or needed. Every segment comes pre-resolved from the immutable <paramref name="chrome" /> (labels formatted and
    ///     clamped, pattern captured once), so this method does NO culture-sensitive or list-dependent work: the measure and
    ///     write passes are pure measure/copy over the same strings and therefore always agree on size (a mismatch would
    ///     overrun the caller's stack-allocated span inside its <c>fixed</c> block).
    /// </summary>
    internal static int BuildFilter(Span<char> destination, FilterChrome chrome)
    {
        var label = chrome.SupportedTypesLabel;
        var pattern = chrome.Pattern;
        var allFiles = chrome.AllFilesLabel;

        // label\0pattern\0allFiles\0*.*\0\0
        var needed = label.Length + 1 + pattern.Length + 1 + allFiles.Length + 1 + "*.*".Length + 1 + 1;

        if (destination.IsEmpty) { return needed; }

        var position = 0;
        position += CopyWithNullTerminator(label, destination[position..]);
        position += CopyWithNullTerminator(pattern, destination[position..]);
        position += CopyWithNullTerminator(allFiles, destination[position..]);
        position += CopyWithNullTerminator("*.*", destination[position..]);
        destination[position] = '\0'; // double-null terminator

        return needed;
    }

    private static OpenFileName BuildOfn(
        IntPtr hwndOwner,
        IntPtr filterPtr,
        IntPtr fileBufferPtr,
        IntPtr titlePtr,
        IntPtr initialDirectoryPtr,
        bool multiSelect) =>
        new()
        {
            lStructSize = OpenFileName.NativeSize,
            hwndOwner = hwndOwner,
            lpstrFilter = filterPtr,
            nFilterIndex = 1,
            lpstrFile = fileBufferPtr,
            nMaxFile = FileBufferChars,
            lpstrInitialDir = initialDirectoryPtr,
            lpstrTitle = titlePtr,
            Flags = OFN_EXPLORER |
                OFN_PATHMUSTEXIST |
                OFN_FILEMUSTEXIST |
                OFN_HIDEREADONLY |
                OFN_NOCHANGEDIR |
                OFN_DONTADDTORECENT |
                (multiSelect ? OFN_ALLOWMULTISELECT : 0)
        };

    private static string Clamp(string value) =>
        value.Length <= MaxFilterLabelChars ? value : value[..MaxFilterLabelChars];

    [LibraryImport("Comdlg32.dll", EntryPoint = "CommDlgExtendedError")]
    private static partial int CommDlgExtendedError();

    private static int CopyNullableInitialDirectory(Span<char> destination, string? initialDirectory)
    {
        if (string.IsNullOrEmpty(initialDirectory)) { return 0; }

        var initialDirectoryLength = Math.Min(initialDirectory.Length, FileBufferChars - 1);
        var needed = initialDirectoryLength + 1;
        if (destination.IsEmpty) { return needed; }

        initialDirectory.AsSpan(0, initialDirectoryLength).CopyTo(destination);
        destination[initialDirectoryLength] = '\0';
        return needed;
    }

    /// <summary>
    ///     Builds (or measures, when <paramref name="destination" /> is empty) the null-terminated title buffer. Returns
    ///     0 when <paramref name="title" /> is null or empty (the dialog uses its default title in that case — pass
    ///     IntPtr.Zero for <c>lpstrTitle</c>). Otherwise returns the buffer size needed (clamped title length + 1 for the null
    ///     terminator). Titles longer than <see cref="MaxTitleChars" /> are silently truncated to bound the stack allocation
    ///     against external input (mirror of the suggested-filename guard in <see cref="PickSaveFile" />).
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
