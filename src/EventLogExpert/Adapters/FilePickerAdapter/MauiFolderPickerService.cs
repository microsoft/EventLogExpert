// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Platforms.Windows;
using EventLogExpert.Runtime.Common.Files;

namespace EventLogExpert.Adapters.FilePickerAdapter;

/// <summary>MAUI adapter that delegates to <see cref="FolderPickerHelper.PickFolderAsync"/>.</summary>
internal sealed class MauiFolderPickerService : IFolderPickerService
{
    public Task<string?> PickFolderAsync() => FolderPickerHelper.PickFolderAsync();
}
