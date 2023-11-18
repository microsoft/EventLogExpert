// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Store.FilterPane;

public sealed record FilterPaneAction
{
    public sealed record AddFilter(FilterModel? FilterModel = null);

    public sealed record SetFilter(FilterModel FilterModel);

    public sealed record ToggleEnableFilter(Guid Id);

    public sealed record ToggleEditFilter(Guid Id);

    public sealed record RemoveFilter(Guid Id);

    public sealed record AddSubFilter(Guid ParentId);

    public sealed record RemoveSubFilter(Guid ParentId, Guid SubFilterId);

    public sealed record SetFilterDateRange(FilterDateModel? FilterDateModel);

    public sealed record ToggleFilterDate;

    public sealed record SetAdvancedFilter(AdvancedFilterModel? AdvancedFilterModel);

    public sealed record ToggleAdvancedFilter;

    public sealed record AddCachedFilter(AdvancedFilterModel AdvancedFilterModel);

    public sealed record RemoveCachedFilter(AdvancedFilterModel AdvancedFilterModel);

    public sealed record ToggleCachedFilter(AdvancedFilterModel AdvancedFilterModel);

    public sealed record ClearAllFilters();
}
