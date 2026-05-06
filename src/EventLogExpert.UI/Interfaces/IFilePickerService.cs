// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Interfaces;

public interface IFilePickerService
{
    /// <summary>
    ///     Opens a system "Open File" dialog filtered to <paramref name="extensions" />; returns the picked path or
    ///     <c>null</c> if the user cancelled.
    /// </summary>
    Task<string?> PickAsync(string pickerTitle, IReadOnlyList<string> extensions);

    /// <summary>
    ///     Opens a multi-select "Open File" dialog filtered to <paramref name="extensions" />; returns the picked
    ///     paths (empty if the user cancelled).
    /// </summary>
    Task<IReadOnlyList<string>> PickMultipleAsync(string pickerTitle, IReadOnlyList<string> extensions);
}

public static class FilePickerServiceFileTypes
{
    public static readonly IReadOnlyList<string> Database = [".db", ".zip"];
    public static readonly IReadOnlyList<string> Json = [".json"];
}
