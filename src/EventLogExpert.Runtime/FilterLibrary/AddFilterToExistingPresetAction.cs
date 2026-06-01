// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;

namespace EventLogExpert.Runtime.FilterLibrary;

internal sealed record AddFilterToExistingPresetAction(LibraryEntryId PresetId, SavedFilter Filter, LibraryEntryId? SourceEntryId);
