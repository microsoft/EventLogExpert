// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Platforms.Windows;

internal static class Win32FileDialogService
{
    public static Task<string?> PickAsync(IReadOnlyList<string> extensions, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(extensions);

        var hwnd = PickerHostWindow.GetHandle();

        return RunOnStaThreadAsync(() => Win32FileDialog.PickSingleFile(hwnd, extensions, title));
    }

    public static Task<string?> PickFolderAsync(string? title = null)
    {
        var hwnd = PickerHostWindow.GetHandle();

        return RunOnStaThreadAsync(() => Win32FolderDialog.PickFolder(hwnd, title));
    }

    public static Task<IReadOnlyList<string>> PickMultipleAsync(IReadOnlyList<string> extensions, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(extensions);

        var hwnd = PickerHostWindow.GetHandle();

        return RunOnStaThreadAsync(() => Win32FileDialog.PickMultipleFiles(hwnd, extensions, title));
    }

    public static Task<string?> PickSaveAsync(IReadOnlyList<string> extensions, string? suggestedFileName = null, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(extensions);

        var hwnd = PickerHostWindow.GetHandle();

        return RunOnStaThreadAsync(() => Win32FileDialog.PickSaveFile(hwnd, extensions, suggestedFileName, title));
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


