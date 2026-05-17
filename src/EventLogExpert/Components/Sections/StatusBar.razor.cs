// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Runtime;
using EventLogExpert.UI.EventLog;
using EventLogExpert.UI.LogTable;
using EventLogExpert.UI.StatusBar;
using Fluxor;
using Microsoft.AspNetCore.Components;
using System.Collections.Immutable;

namespace EventLogExpert.Components.Sections;

public sealed partial class StatusBar
{
    [Inject] private IStateSelection<EventLogState, (
        ImmutableDictionary<string, EventLogData> ActiveLogs,
        Filter AppliedFilter,
        bool ContinuouslyUpdate,
        IReadOnlyList<ResolvedEvent> NewEventBuffer,
        bool NewEventBufferIsFull)> EventLogSelection
    { get; init; } = null!;

    [Inject] private IStateSelection<LogTableState, (
        EventLogId? ActiveEventLogId,
        ImmutableList<LogView> EventTables,
        IReadOnlyList<ResolvedEvent> DisplayedEvents,
        ImmutableDictionary<EventLogId, int> EventCountByLog)> LogTableSelection
    { get; init; } = null!;

    [Inject] private IStateSelection<StatusBarState, (
        ImmutableDictionary<StatusActivityId, (int, int)> EventsLoading,
        string ResolverStatus)> StatusBarSelection
    { get; init; } = null!;

    protected override void OnInitialized()
    {
        EventLogSelection.Select(static s =>
            (s.ActiveLogs, s.AppliedFilter, s.ContinuouslyUpdate, s.NewEventBuffer, s.NewEventBufferIsFull));
        LogTableSelection.Select(static s =>
            (s.ActiveEventLogId, s.EventTables, s.DisplayedEvents, s.EventCountByLog));
        StatusBarSelection.Select(static s => (s.EventsLoading, s.ResolverStatus));

        base.OnInitialized();
    }
}
