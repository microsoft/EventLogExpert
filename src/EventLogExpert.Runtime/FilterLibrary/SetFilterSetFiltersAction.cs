// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterLibrary;

internal sealed record SetFilterSetFiltersAction(LibraryEntryId FilterSetId, ImmutableList<SavedFilter> Filters);
