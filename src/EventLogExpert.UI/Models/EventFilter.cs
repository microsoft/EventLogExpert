// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.UI.Models;

public sealed record EventFilter(
    AdvancedFilterModel? AdvancedFilter,
    FilterDateModel? DateFilter,
    ImmutableList<AdvancedFilterModel> CachedFilters,
    ImmutableList<FilterModel> Filters);
