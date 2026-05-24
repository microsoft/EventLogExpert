// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Common.Files;

public interface IFilePickerService
{
    /// <summary>
    ///     Opens a system "Open File" dialog filtered to <paramref name="extensions" />; returns the picked path or
    ///     <c>null</c> if the user cancelled.
    /// </summary>
    Task<string?> PickAsync(string pickerTitle, IReadOnlyList<string> extensions);

    /// <summary>
    ///     Opens a multi-select "Open File" dialog filtered to <paramref name="extensions" />; returns the picked paths
    ///     (empty if the user cancelled).
    /// </summary>
    Task<IReadOnlyList<string>> PickMultipleAsync(string pickerTitle, IReadOnlyList<string> extensions);

    /// <summary>
    ///     Opens a system "Save As" dialog filtered to <paramref name="extensions" /> (first entry is the default
    ///     extension auto-appended when the user types a name without one). The dialog prompts before overwriting an existing
    ///     file. Returns the picked path or <c>null</c> if the user cancelled. The caller is responsible for actually writing
    ///     to the path — this method only picks the destination.
    /// </summary>
    Task<string?> PickSaveAsync(string pickerTitle, IReadOnlyList<string> extensions, string? suggestedFileName = null);
}
