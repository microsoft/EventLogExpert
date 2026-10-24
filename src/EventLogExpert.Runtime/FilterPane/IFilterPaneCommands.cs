// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;

namespace EventLogExpert.Runtime.FilterPane;

public interface IFilterPaneCommands
{
    /// <summary>Clears all filters from the pane (date filter + saved filters + pending drafts).</summary>
    void ClearAllFilters();

    /// <summary>
    ///     Hides events newer than <paramref name="timeCreatedUtc" />: narrows an enabled date filter's end bound (never
    ///     widening it, keeping its start bound), or replaces a disabled or missing filter with one ending there.
    /// </summary>
    void ExcludeEventsAfter(DateTime timeCreatedUtc);

    /// <summary>
    ///     Hides events older than <paramref name="timeCreatedUtc" />: narrows an enabled date filter's start bound
    ///     (never widening it, keeping its end bound), or replaces a disabled or missing filter with one starting there.
    /// </summary>
    void ExcludeEventsBefore(DateTime timeCreatedUtc);

    /// <summary>Removes the filter with <paramref name="id" /> from the pane.</summary>
    void RemoveFilter(FilterId id);

    /// <summary>Adds or replaces <paramref name="filter" /> in the pane (upsert by FilterId).</summary>
    void SetFilter(SavedFilter filter);

    /// <summary>
    ///     Replaces the pane's date-range filter as given (<see langword="null" /> clears it); a null bound leaves that
    ///     side of the range open.
    /// </summary>
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
