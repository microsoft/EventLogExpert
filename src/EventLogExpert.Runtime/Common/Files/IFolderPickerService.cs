// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Common.Files;

/// <summary>
///     Abstracts the platform-specific folder picker dialog so UI components can be unit-tested without spinning up
///     the real WinUI picker.
/// </summary>
public interface IFolderPickerService
{
    /// <summary>
    ///     Opens a system "Select Folder" dialog. Returns the picked folder path, or <c>null</c> when the user cancelled.
    /// </summary>
    Task<string?> PickFolderAsync();
}
