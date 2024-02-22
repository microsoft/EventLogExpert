// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.UI.Models;

public readonly record struct EventFilter(
    FilterDateModel? DateFilter,
    ImmutableList<FilterModel> AdvancedFilters,
    ImmutableList<FilterModel> BasicFilters,
    ImmutableList<FilterModel> CachedFilters);
