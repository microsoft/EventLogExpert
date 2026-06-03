// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;

namespace EventLogExpert.Runtime.FilterPane;

public interface IFilterPaneCommands
{
    /// <summary>Clears all filters from the pane (date filter + saved filters + pending drafts).</summary>
    void ClearAllFilters();

    /// <summary>Removes the filter with <paramref name="id" /> from the pane.</summary>
    void RemoveFilter(FilterId id);

    /// <summary>Adds or replaces <paramref name="filter" /> in the pane (upsert by FilterId).</summary>
    void SetFilter(SavedFilter filter);

    /// <summary>Sets the pane's date-range filter (<see langword="null" /> clears it).</summary>
    void SetFilterDateRange(DateFilter? dateFilter);

    /// <summary>Sets whether the filter with <paramref name="id" /> excludes matching events.</summary>
    void SetFilterExcluded(FilterId id, bool isExcluded);

    /// <summary>Toggles whether the active date range filter is applied.</summary>
    void ToggleFilterDate();

    /// <summary>Toggles whether the filter with <paramref name="id" /> is active.</summary>
    void ToggleFilterEnabled(FilterId id);

    /// <summary>Toggles whether the filter with <paramref name="id" /> excludes matching events.</summary>
    void ToggleFilterExcluded(FilterId id);

    /// <summary>Flips the filter pane's master enabled flag (the menu's "Show All Events" toggle).</summary>
    void ToggleFilteringEnabled();
}
