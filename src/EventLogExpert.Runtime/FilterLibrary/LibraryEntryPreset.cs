// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterLibrary;

public sealed record LibraryEntryPreset : LibraryEntry
{
    public required ImmutableList<SavedFilter> Filters { get; init; }
}
