// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Store.FilterPane;

public sealed record FilterPaneAction
{
    public sealed record AddFilter(FilterModel? FilterModel = null);

    public sealed record AddSubFilter(FilterId ParentId);

    public sealed record ApplyFilterGroup(FilterGroupModel FilterGroup);

    public sealed record ClearAllFilters;

    public sealed record RemoveFilter(FilterId Id);

    public sealed record RemoveSubFilter(FilterId ParentId, FilterId SubFilterId);

    public sealed record SaveFilterGroup(string Name);

    public sealed record SetFilter(FilterModel FilterModel);

    public sealed record SetFilterDateRange(FilterDateModel? FilterDateModel);

    public sealed record SetFilterDateRangeSuccess(FilterDateModel? FilterDateModel);

    public sealed record ToggleFilterEditing(FilterId Id);

    public sealed record ToggleFilterEnabled(FilterId Id);

    public sealed record ToggleFilterExcluded(FilterId Id);

    public sealed record ToggleFilterDate;

    public sealed record ToggleIsEnabled;

    public sealed record ToggleIsLoading;

    public sealed record ToggleIsXmlEnabled;
}
