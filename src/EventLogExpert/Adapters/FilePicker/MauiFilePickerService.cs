// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Platforms.Windows;
using EventLogExpert.Runtime.Common.Files;

namespace EventLogExpert.Adapters.FilePicker;

/// <summary>MAUI adapter that delegates to <see cref="Win32FileDialogService" /> for Win32 file picking.</summary>
public sealed class MauiFilePickerService : IFilePickerService
{
    public Task<string?> PickAsync(string pickerTitle, IReadOnlyList<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(pickerTitle);
        ArgumentNullException.ThrowIfNull(extensions);

        if (extensions.Count == 0)
        {
            throw new ArgumentException(
                "At least one extension must be supplied.", nameof(extensions));
        }

        return MainThread.InvokeOnMainThreadAsync(() => Win32FileDialogService.PickAsync(extensions, pickerTitle));
    }

    public Task<IReadOnlyList<string>> PickMultipleAsync(
        string pickerTitle,
        IReadOnlyList<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(pickerTitle);
        ArgumentNullException.ThrowIfNull(extensions);

        if (extensions.Count == 0)
        {
            throw new ArgumentException(
                "At least one extension must be supplied.", nameof(extensions));
        }

        return MainThread.InvokeOnMainThreadAsync(() => Win32FileDialogService.PickMultipleAsync(extensions, pickerTitle));
    }

    public Task<string?> PickSaveAsync(
        string pickerTitle,
        IReadOnlyList<string> extensions,
        string? suggestedFileName = null)
    {
        ArgumentNullException.ThrowIfNull(pickerTitle);
        ArgumentNullException.ThrowIfNull(extensions);

        if (extensions.Count == 0)
        {
            throw new ArgumentException(
                "At least one extension must be supplied.", nameof(extensions));
        }

        return MainThread.InvokeOnMainThreadAsync(() => Win32FileDialogService.PickSaveAsync(extensions, suggestedFileName, pickerTitle));
    }
}
