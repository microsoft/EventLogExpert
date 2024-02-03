// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Store.FilterPane;

public sealed record FilterPaneAction
{
    public sealed record AddAdvancedFilter(FilterModel? FilterModel = null);

    public sealed record AddBasicFilter(FilterModel? FilterModel = null);

    public sealed record AddCachedFilter(FilterModel FilterModel);

    public sealed record AddSubFilter(Guid ParentId);

    public sealed record ApplyFilterGroup(FilterGroupModel FilterGroup);

    public sealed record ClearAllFilters;

    public sealed record RemoveAdvancedFilter(Guid Id);

    public sealed record RemoveBasicFilter(Guid Id);

    public sealed record RemoveCachedFilter(Guid Id);

    public sealed record RemoveSubFilter(Guid ParentId, Guid SubFilterId);

    public sealed record SaveFilterGroup(string Name);

    public sealed record SetAdvancedFilter(FilterModel FilterModel);

    public sealed record SetBasicFilter(FilterModel FilterModel);

    public sealed record SetCachedFilter(FilterModel FilterModel);

    public sealed record SetFilterDateRange(FilterDateModel? FilterDateModel);

    public sealed record ToggleAdvancedFilterEditing(Guid Id);

    public sealed record ToggleAdvancedFilterEnabled(Guid Id);

    public sealed record ToggleBasicFilterEditing(Guid Id);

    public sealed record ToggleBasicFilterEnabled(Guid Id);

    public sealed record ToggleCachedFilterEditing(Guid Id);

    public sealed record ToggleCachedFilterEnabled(Guid Id);

    public sealed record ToggleFilterDate;

    public sealed record ToggleIsEnabled;

    public sealed record ToggleIsLoading;
}
