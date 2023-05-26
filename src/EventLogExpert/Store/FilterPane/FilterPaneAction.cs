// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;

namespace EventLogExpert.Store.FilterPane;

public record FilterPaneAction
{
    public record AddFilter;

    public record ToggleFilter(Guid Id);

    public record RemoveFilter(FilterModel FilterModel);

    public record AddSubFilter(Guid ParentId);

    public record RemoveSubFilter(Guid ParentId, SubFilterModel SubFilterModel);

    public record ApplyFilters;

    public record SetFilterDateRange(FilterDateModel? FilterDateModel);

    public record ToggleFilterDate();

    public record SetAdvancedFilter(string Expression);

    public record ToggleAdvancedFilter();
}
