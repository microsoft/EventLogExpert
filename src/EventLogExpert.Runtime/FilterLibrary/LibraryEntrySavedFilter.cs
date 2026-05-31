// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;

namespace EventLogExpert.Runtime.FilterLibrary;

public sealed record LibraryEntrySavedFilter(string Id, string Name, DateTimeOffset CreatedUtc, SavedFilter Filter)
    : LibraryEntry(Id, Name, CreatedUtc);
