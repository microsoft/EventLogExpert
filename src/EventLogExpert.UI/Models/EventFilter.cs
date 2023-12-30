// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.UI.Models;

public sealed record EventFilter(
    FilterModel? AdvancedFilter,
    FilterDateModel? DateFilter,
    ImmutableList<FilterModel> CachedFilters,
    ImmutableList<FilterModel> Filters);
