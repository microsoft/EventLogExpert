// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;

namespace EventLogExpert.UI.FilterEditor;

/// <summary>
///     Cascaded capability that lets a filter row's action cluster export that single row as scenario-catalog JSON.
///     It is provided once per container - the filter pane (active rows) and the filter-library modal (saved-group rows) -
///     and only when scenario authoring is enabled, so the per-row button appears solely where a container opts in and is
///     absent everywhere else. A developer authoring aid.
/// </summary>
/// <param name="Enabled">Whether the per-row "Copy as scenario JSON" button should render.</param>
/// <param name="CopyAsync">
///     Exports the supplied row as scenario JSON (copies to the clipboard) and reports the outcome to
///     the user.
/// </param>
public sealed record ScenarioAuthoringRowContext(bool Enabled, Func<SavedFilter, Task> CopyAsync)
{
    public Func<SavedFilter, Task> CopyAsync { get; init; } = CopyAsync ?? throw new ArgumentNullException(nameof(CopyAsync));
}
