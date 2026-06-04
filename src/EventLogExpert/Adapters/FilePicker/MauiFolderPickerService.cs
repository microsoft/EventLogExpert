// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Platforms.Windows;
using EventLogExpert.Runtime.Common.Files;

namespace EventLogExpert.Adapters.FilePicker;

/// <summary>MAUI adapter that delegates to <see cref="WinUiFolderPicker.PickFolderAsync"/>.</summary>
internal sealed class MauiFolderPickerService : IFolderPickerService
{
    public Task<string?> PickFolderAsync() => WinUiFolderPicker.PickFolderAsync();
}
