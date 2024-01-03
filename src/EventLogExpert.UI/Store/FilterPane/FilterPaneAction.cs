// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Store.FilterPane;

public sealed record FilterPaneAction
{
    public sealed record AddCachedFilter(FilterModel FilterModel);

    public sealed record AddFilter(FilterModel? FilterModel = null);

    public sealed record AddSubFilter(Guid ParentId);

    public sealed record ClearAllFilters;

    public sealed record RemoveCachedFilter(FilterModel FilterModel);

    public sealed record RemoveFilter(Guid Id);

    public sealed record RemoveSubFilter(Guid ParentId, Guid SubFilterId);

    public sealed record SetAdvancedFilter(FilterModel? FilterModel);

    public sealed record SetAdvancedFilterCompleted(FilterModel? FilterModel);

    public sealed record SetFilter(FilterModel FilterModel);

    public sealed record SetFilterDateRange(FilterDateModel? FilterDateModel);

    public sealed record ToggleAdvancedFilter;

    public sealed record ToggleCachedFilter(FilterModel FilterModel);

    public sealed record ToggleEnableFilter(Guid Id);

    public sealed record ToggleEditFilter(Guid Id);

    public sealed record ToggleFilterDate;

    public sealed record ToggleIsEnabled;

    public sealed record ToggleIsLoading;
}
