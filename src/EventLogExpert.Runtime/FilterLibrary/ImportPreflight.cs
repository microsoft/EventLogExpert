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
        : this(toAdd, toReplace, skippedDuplicates, [], [], error)
    {
    }

    public ImportPreflight(
        IReadOnlyList<LibraryEntry> toAdd,
        IReadOnlyList<(LibraryEntry Existing, LibraryEntry Incoming)> toReplace,
        IReadOnlyList<LibraryEntry> skippedDuplicates,
        IReadOnlyList<(LibraryEntry Existing, LibraryEntry Incoming)> toUpdate,
        IReadOnlyList<(IReadOnlyList<LibraryEntry> Candidates, LibraryEntry Incoming)> ambiguousMatches,
        string? error = null)
    {
        ArgumentNullException.ThrowIfNull(toAdd);
        ArgumentNullException.ThrowIfNull(toReplace);
        ArgumentNullException.ThrowIfNull(skippedDuplicates);
        ArgumentNullException.ThrowIfNull(toUpdate);
        ArgumentNullException.ThrowIfNull(ambiguousMatches);

        ToAdd = toAdd;
        ToReplace = toReplace;
        SkippedDuplicates = skippedDuplicates;
        ToUpdate = toUpdate;
        AmbiguousMatches = ambiguousMatches;
        Error = error;
        InvalidLegacyNames = [];
    }

    public static ImportPreflight Blocked(IReadOnlyList<string> invalidLegacyNames)
    {
        ArgumentNullException.ThrowIfNull(invalidLegacyNames);

        return new ImportPreflight([], [], [], [], [])
        {
            ImportBlocked = true,
            InvalidLegacyNames = invalidLegacyNames,
        };
    }

    public IReadOnlyList<LibraryEntry> ToAdd { get; }

    public IReadOnlyList<(LibraryEntry Existing, LibraryEntry Incoming)> ToReplace { get; }

    public IReadOnlyList<LibraryEntry> SkippedDuplicates { get; }

    public IReadOnlyList<(LibraryEntry Existing, LibraryEntry Incoming)> ToUpdate { get; }

    public IReadOnlyList<(IReadOnlyList<LibraryEntry> Candidates, LibraryEntry Incoming)> AmbiguousMatches { get; }

    public string? Error { get; }

    public bool ImportBlocked { get; init; }

    public IReadOnlyList<string> InvalidLegacyNames { get; init; }
}
