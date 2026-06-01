// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;

namespace EventLogExpert.Runtime.FilterLibrary;

public sealed record LibraryEntrySavedFilter : LibraryEntry
{
    public required SavedFilter Filter { get; init; }
}
