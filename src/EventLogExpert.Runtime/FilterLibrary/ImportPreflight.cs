// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.FilterLibrary;

public sealed record ImportPreflight
{
    public ImportPreflight(
        IReadOnlyList<LibraryEntry> toAdd,
        IReadOnlyList<(LibraryEntry Existing, LibraryEntry Incoming)> toReplace,
        IReadOnlyList<LibraryEntry> skippedDuplicates,
        string? error = null)
    {
        ArgumentNullException.ThrowIfNull(toAdd);
        ArgumentNullException.ThrowIfNull(toReplace);
        ArgumentNullException.ThrowIfNull(skippedDuplicates);

        ToAdd = toAdd;
        ToReplace = toReplace;
        SkippedDuplicates = skippedDuplicates;
        Error = error;
    }

    public IReadOnlyList<LibraryEntry> ToAdd { get; }

    public IReadOnlyList<(LibraryEntry Existing, LibraryEntry Incoming)> ToReplace { get; }

    public IReadOnlyList<LibraryEntry> SkippedDuplicates { get; }

    public string? Error { get; }
}
