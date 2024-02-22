// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Store.FilterPane;

public sealed record FilterPaneAction
{
    public sealed record AddAdvancedFilter(FilterModel? FilterModel = null);

    public sealed record AddBasicFilter(FilterModel? FilterModel = null);

    public sealed record AddCachedFilter(FilterModel FilterModel);

    public sealed record AddSubFilter(FilterId ParentId);

    public sealed record ApplyFilterGroup(FilterGroupModel FilterGroup);

    public sealed record ClearAllFilters;

    public sealed record RemoveAdvancedFilter(FilterId Id);

    public sealed record RemoveBasicFilter(FilterId Id);

    public sealed record RemoveCachedFilter(FilterId Id);

    public sealed record RemoveSubFilter(FilterId ParentId, FilterId SubFilterId);

    public sealed record SaveFilterGroup(string Name);

    public sealed record SetAdvancedFilter(FilterModel FilterModel);

    public sealed record SetBasicFilter(FilterModel FilterModel);

    public sealed record SetCachedFilter(FilterModel FilterModel);

    public sealed record SetFilterDateRange(FilterDateModel? FilterDateModel);

    public sealed record ToggleAdvancedFilterEditing(FilterId Id);

    public sealed record ToggleAdvancedFilterEnabled(FilterId Id);

    public sealed record ToggleBasicFilterEditing(FilterId Id);

    public sealed record ToggleBasicFilterEnabled(FilterId Id);

    public sealed record ToggleCachedFilterEditing(FilterId Id);

    public sealed record ToggleCachedFilterEnabled(FilterId Id);

    public sealed record ToggleFilterDate;

    public sealed record ToggleIsEnabled;

    public sealed record ToggleIsLoading;
}
