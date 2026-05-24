// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Platforms.Windows;

/// <summary>
///     Presents the Win32 file dialogs for open (single + multi) and save scenarios. Delegates to
///     <see cref="Win32FileDialog" />, which uses the procedural <c>comdlg32!GetOpenFileNameW</c> /
///     <c>GetSaveFileNameW</c> APIs (NO COM activation) rather than WinUI's <c>FileOpenPicker</c> / <c>FileSavePicker</c>
///     or the shell <c>IFileOpenDialog</c> COM class — both of which throw <c>COMException 0x80004005</c> under elevated
///     processes (even with <c>InitializeWithWindow</c> applied) or <c>REGDB_E_CLASSNOTREG</c> under MSIX-packaged
///     elevated processes. Returns the selected file path(s), or <c>null</c> / an empty list when the user cancelled.
/// </summary>
/// <remarks>
///     The dialogs MUST run on a dedicated <see cref="ApartmentState.STA" /> thread. Although the procedural
///     <c>GetOpenFileNameW</c> / <c>GetSaveFileNameW</c> APIs don't require COM activation, they DO require an STA
///     apartment so the modern explorer-style dialog (<c>OFN_EXPLORER</c>) can host the shell common-controls that depend
///     on STA-only OLE drag-and-drop, IDataObject marshalling, and the explorer view's own threading model. Running them
///     on the MAUI <see cref="MainThread" /> (the dispatcher queue thread) is unreliable because its apartment state is
///     not guaranteed to be STA under elevated WinUI hosts. Creating our own STA thread via <see cref="Thread" /> +
///     <see cref="Thread.SetApartmentState(ApartmentState)" /> gives the dialog a clean, well-defined apartment to present
///     from. The dialog APIs pump their own message loop until dismissed, so the STA thread doesn't need a
///     <c>Dispatcher.Run</c> set up around it.
/// </remarks>
internal static class FilePickerHelper
{
    public static Task<string?> PickAsync(IReadOnlyList<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);

        var hwnd = PickerHostWindow.GetHandle();

        return RunOnStaThreadAsync(() => Win32FileDialog.PickSingleFile(hwnd, extensions));
    }

    public static Task<IReadOnlyList<string>> PickMultipleAsync(IReadOnlyList<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);

        var hwnd = PickerHostWindow.GetHandle();

        return RunOnStaThreadAsync(() => Win32FileDialog.PickMultipleFiles(hwnd, extensions));
    }

    public static Task<string?> PickSaveAsync(IReadOnlyList<string> extensions, string? suggestedFileName = null)
    {
        ArgumentNullException.ThrowIfNull(extensions);

        var hwnd = PickerHostWindow.GetHandle();

        return RunOnStaThreadAsync(() => Win32FileDialog.PickSaveFile(hwnd, extensions, suggestedFileName));
    }

    private static Task<T> RunOnStaThreadAsync<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        })
        {
            IsBackground = true,
            Name = "Win32FileDialog STA"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return tcs.Task;
    }
}


