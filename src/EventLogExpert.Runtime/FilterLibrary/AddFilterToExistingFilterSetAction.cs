// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;

namespace EventLogExpert.Runtime.FilterLibrary;

internal sealed record AddFilterToExistingFilterSetAction(LibraryEntryId FilterSetId, SavedFilter Filter, LibraryEntryId? SourceEntryId);
