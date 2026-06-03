// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.FilterLibrary;

namespace EventLogExpert.UI.FilterLibrary;

public sealed record AddToFilterSetIntent(
    SavedFilter Filter,
    LibraryEntryId? FilterSetId,
    string? NewFilterSetName,
    LibraryEntryId SourceEntryId)
{
    public SavedFilter Filter { get; init; } = Filter ?? throw new ArgumentNullException(nameof(Filter));
}
