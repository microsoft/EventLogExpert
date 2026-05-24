// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Platforms.Windows;

/// <summary>
///     Presents the file-open dialog for single- and multi-select scenarios. Delegates to
///     <see cref="Win32FileDialog" /> (Win32 <c>IFileOpenDialog</c> COM interop) rather than WinUI's <c>FileOpenPicker</c>
///     because the WinUI picker throws <c>COMException 0x80004005</c> under elevated processes — even with
///     <c>InitializeWithWindow</c> applied. Returns the selected file path(s), or <c>null</c> / an empty list when the
///     user cancelled.
/// </summary>
/// <remarks>
///     The dialog MUST run on a dedicated <see cref="ApartmentState.STA" /> thread. <see cref="MainThread" /> in
///     MAUI/WinUI on Windows resolves to the dispatcher queue thread, whose COM apartment state is not reliably STA when
///     the host is elevated — <c>CoCreateInstance</c> for <c>FileOpenDialog</c> from that thread fails with
///     <c>REGDB_E_CLASSNOTREG</c>. Creating our own STA thread via <see cref="Thread" /> +
///     <see cref="Thread.SetApartmentState(ApartmentState)" /> gives the dialog a clean, well-defined apartment to
///     activate into. The dialog's <c>Show</c> pumps its own message loop until dismissed, so the thread doesn't need a
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


