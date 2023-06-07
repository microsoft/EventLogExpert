// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.Store.FilterPane;

public record FilterPaneAction
{
    public record AddFilter(FilterModel? FilterModel = null);

    public record SetFilter(FilterModel FilterModel);

    public record ToggleEnableFilter(Guid Id);

    public record ToggleEditFilter(Guid Id);

    public record RemoveFilter(Guid Id);

    public record AddSubFilter(Guid ParentId, FilterMode? FilterMode = null);

    public record RemoveSubFilter(Guid ParentId, Guid SubFilterId);

    public record SetFilterDateRange(FilterDateModel? FilterDateModel);

    public record ToggleFilterDate;

    public record SetAdvancedFilter(string Expression);

    public record ToggleAdvancedFilter;

    public record SetNumberOfFilteredEvents(int NumberOfFilteredEvents);
}
