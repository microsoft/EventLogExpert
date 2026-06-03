// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.FilterLibrary;

namespace EventLogExpert.UI.FilterLibrary;

public sealed record AddToPresetIntent(
    SavedFilter Filter,
    LibraryEntryId? PresetId,
    string? NewPresetName,
    LibraryEntryId SourceEntryId)
{
    public SavedFilter Filter { get; init; } = Filter ?? throw new ArgumentNullException(nameof(Filter));
}
