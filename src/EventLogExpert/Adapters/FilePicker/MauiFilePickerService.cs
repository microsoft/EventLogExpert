// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Platforms.Windows;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.WindowsPlatform.Dialogs;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.Adapters.FilePicker;

/// <summary>MAUI adapter that delegates to <see cref="Win32FileDialogService" /> for Win32 file picking.</summary>
public sealed class MauiFilePickerService(IStringLocalizer<SharedResource> localizer) : IFilePickerService
{
    private readonly IStringLocalizer<SharedResource> _localizer =
        localizer ?? throw new ArgumentNullException(nameof(localizer));

    public Task<string?> PickAsync(
        string pickerTitle,
        IReadOnlyList<string> extensions,
        string? initialDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(pickerTitle);
        ArgumentNullException.ThrowIfNull(extensions);

        // Snapshot before validating so the checked list is exactly what the STA worker consumes (the caller's
        // IReadOnlyList is not guaranteed immutable).
        string[] extensionSnapshot = [.. extensions];

        if (extensionSnapshot.Length == 0)
        {
            throw new ArgumentException(_localizer["FilePicker_Error_NoExtensions"], nameof(extensions));
        }

        return MainThread.InvokeOnMainThreadAsync(() =>
            Win32FileDialogService.PickAsync(extensionSnapshot, CreateFilterChrome(extensionSnapshot), pickerTitle, initialDirectory));
    }

    public Task<IReadOnlyList<string>> PickMultipleAsync(
        string pickerTitle,
        IReadOnlyList<string> extensions,
        string? initialDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(pickerTitle);
        ArgumentNullException.ThrowIfNull(extensions);

        // Snapshot before validating so the checked list is exactly what the STA worker consumes (the caller's
        // IReadOnlyList is not guaranteed immutable).
        string[] extensionSnapshot = [.. extensions];

        if (extensionSnapshot.Length == 0)
        {
            throw new ArgumentException(_localizer["FilePicker_Error_NoExtensions"], nameof(extensions));
        }

        return MainThread.InvokeOnMainThreadAsync(() =>
            Win32FileDialogService.PickMultipleAsync(extensionSnapshot, CreateFilterChrome(extensionSnapshot), pickerTitle, initialDirectory));
    }

    public Task<string?> PickSaveAsync(
        string pickerTitle,
        IReadOnlyList<string> extensions,
        string? suggestedFileName = null,
        string? initialDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(pickerTitle);
        ArgumentNullException.ThrowIfNull(extensions);

        // Snapshot before validating so the checked list is exactly what the STA worker consumes (the caller's
        // IReadOnlyList is not guaranteed immutable).
        string[] extensionSnapshot = [.. extensions];

        if (extensionSnapshot.Length == 0)
        {
            throw new ArgumentException(_localizer["FilePicker_Error_NoExtensions"], nameof(extensions));
        }

        if (string.IsNullOrWhiteSpace(extensionSnapshot[0]))
        {
            throw new ArgumentException(_localizer["FilePicker_Error_FirstExtensionBlank"], nameof(extensions));
        }

        return MainThread.InvokeOnMainThreadAsync(() =>
            Win32FileDialogService.PickSaveAsync(extensionSnapshot, CreateFilterChrome(extensionSnapshot), suggestedFileName, pickerTitle, initialDirectory));
    }

    // Resolve the localized filter labels on the UI thread (inside the MainThread hand-off), before the native STA
    // worker thread is spawned, because that thread cannot reach the DI localizer.
    private FilterChrome CreateFilterChrome(IReadOnlyList<string> extensions) =>
        Win32FileDialog.CreateFilterChrome(
            _localizer["FilePicker_Filter_SupportedTypes"],
            _localizer["FilePicker_Filter_AllFiles"],
            extensions);
}
