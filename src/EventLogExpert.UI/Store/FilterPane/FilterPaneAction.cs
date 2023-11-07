// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Store.FilterPane;

public record FilterPaneAction
{
    public record AddFilter(FilterModel? FilterModel = null);

    public record SetFilter(FilterModel FilterModel);

    public record ToggleEnableFilter(Guid Id);

    public record ToggleEditFilter(Guid Id);

    public record RemoveFilter(Guid Id);

    public record AddSubFilter(Guid ParentId);

    public record RemoveSubFilter(Guid ParentId, Guid SubFilterId);

    public record SetFilterDateRange(FilterDateModel? FilterDateModel);

    public record ToggleFilterDate;

    public record SetAdvancedFilter(AdvancedFilterModel? AdvancedFilterModel);

    public record ToggleAdvancedFilter;

    public record AddCachedFilter(AdvancedFilterModel AdvancedFilterModel);

    public record RemoveCachedFilter(AdvancedFilterModel AdvancedFilterModel);

    public record ToggleCachedFilter(AdvancedFilterModel AdvancedFilterModel);

    public record ClearAllFilters();
}
