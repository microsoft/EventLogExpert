// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Platforms.Windows;
using EventLogExpert.Runtime.Common.Files;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.Adapters.FilePicker;

internal sealed class MauiFolderPickerService(IStringLocalizer<SharedResource> localizer) : IFolderPickerService
{
    private readonly IStringLocalizer<SharedResource> _localizer = localizer;

    public Task<string?> PickFolderAsync() =>
        MainThread.InvokeOnMainThreadAsync(
            () => Win32FileDialogService.PickFolderAsync(_localizer["FilePicker_Title_SelectFolder"]));
}
