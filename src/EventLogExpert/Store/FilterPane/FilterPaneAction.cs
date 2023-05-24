// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;

namespace EventLogExpert.Store.FilterPane;

public record FilterPaneAction
{
    public record AddAvailableFilters(string FilterText);

    public record AddFilter;

    public record RemoveFilter(FilterModel FilterModel);

    public record AddSubFilter(Guid ParentId);

    public record RemoveSubFilter(Guid ParentId, SubFilterModel SubFilterModel);

    public record ApplyFilters;

    public record SetFilterDateRange(FilterDateModel? FilterDateModel);
}
