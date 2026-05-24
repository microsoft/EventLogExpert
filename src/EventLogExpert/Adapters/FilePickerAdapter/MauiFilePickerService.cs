// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Platforms.Windows;
using EventLogExpert.Runtime.Common.Files;

namespace EventLogExpert.Adapters.FilePickerAdapter;

/// <summary>MAUI adapter that delegates to <see cref="FilePickerHelper" /> for WinUI file picking.</summary>
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

        return MainThread.InvokeOnMainThreadAsync(() => FilePickerHelper.PickAsync(extensions, pickerTitle));
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

        return MainThread.InvokeOnMainThreadAsync(() => FilePickerHelper.PickMultipleAsync(extensions, pickerTitle));
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

        return MainThread.InvokeOnMainThreadAsync(() => FilePickerHelper.PickSaveAsync(extensions, suggestedFileName, pickerTitle));
    }
}
