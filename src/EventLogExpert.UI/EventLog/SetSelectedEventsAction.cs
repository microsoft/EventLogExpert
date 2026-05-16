// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.UI.EventLog;

/// <summary>
///     Replaces the entire selection with the supplied events, preserving input order and de-duplicating by reference
///     identity, and atomically updates the focused (selected) event. Use this for range selection (Shift+Click,
///     Shift+Arrow), Select All (Ctrl+A), clear (Escape), and any selection update where the caller already knows both the
///     new selection and the new focus.
/// </summary>
/// <param name="SelectedEvents">
///     The new selection list, in the order that should be preserved (typically the current
///     table's sort order).
/// </param>
/// <param name="SelectedEvent">
///     The new focused event, or null to clear focus. Does not need to be a member of
///     <paramref name="SelectedEvents" />.
/// </param>
internal sealed record SetSelectedEventsAction(
    IReadOnlyCollection<ResolvedEvent> SelectedEvents,
    ResolvedEvent? SelectedEvent);
