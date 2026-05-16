// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.FilterPane;

/// <summary>
///     Host-facing intent API for the FilterPane slice. Hides slice-internal action records from consumers outside
///     the UI assembly so the IVT grant to <c>EventLogExpert</c> can be dropped.
/// </summary>
public interface IFilterPaneCommands
{
    /// <summary>Toggles whether the active date range filter is applied.</summary>
    void ToggleFilterDate();

    /// <summary>Flips the filter pane's master enabled flag (the menu's "Show All Events" toggle).</summary>
    void ToggleFilteringEnabled();
}
