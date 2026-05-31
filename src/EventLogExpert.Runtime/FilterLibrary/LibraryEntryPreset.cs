// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterLibrary;

public sealed record LibraryEntryPreset(string Id, string Name, DateTimeOffset CreatedUtc, ImmutableList<SavedFilter> Filters)
    : LibraryEntry(Id, Name, CreatedUtc);
