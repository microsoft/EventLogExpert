// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.StatusBar;
using Fluxor;
using Microsoft.AspNetCore.Components;
using System.Collections.Immutable;

namespace EventLogExpert.UI.StatusBar;

public sealed partial class StatusBar
{
    [Inject]
    private IStateSelection<EventLogState, (
        Filter AppliedFilter,
        bool ContinuouslyUpdate,
        int NewEventBufferCount,
        bool NewEventBufferIsFull,
        int SelectionCount)> EventLogSelection
    { get; init; } = null!;

    [Inject]
    private IStateSelection<FilterPaneState, bool> FilterActiveSelection { get; init; } = null!;

    [Inject]
    private IStateSelection<FilterLensState, int> LensCountSelection { get; init; } = null!;

    [Inject]
    private IStateSelection<LogTableState, (
        EventLogId? ActiveEventLogId,
        ImmutableList<LogView> EventTables,
        int ActiveFilteredViewCount,
        ImmutableDictionary<EventLogId, int> EventCountByLog,
        ImmutableList<LogTabGroup> Groups)> LogTableSelection
    { get; init; } = null!;

    [Inject]
    private IStateSelection<RawEventCountState, (
        int Total,
        ImmutableDictionary<EventLogId, int> ByLog)> RawCountSelection
    { get; init; } = null!;

    [Inject]
    private IStateSelection<StatusBarState, (
        ImmutableDictionary<StatusActivityId, (int, int)> EventsLoading,
        string ResolverStatus)> StatusBarSelection
    { get; init; } = null!;

    protected override void OnInitialized()
    {
        EventLogSelection.Select(static s =>
            (s.AppliedFilter, s.ContinuouslyUpdate, s.NewEventBuffer.Count, s.NewEventBufferIsFull, s.Selection.Count));
        RawCountSelection.Select(static s => (s.Total, s.ByLog));
        LogTableSelection.Select(static s =>
        {
            var activeTable = s.EventTables.FirstOrDefault(table => table.Id == s.ActiveEventLogId);

            return (
                s.ActiveEventLogId,
                s.EventTables,
                activeTable is null ? 0 : s.DisplayedEventsForTab(activeTable).Count,
                s.EventCountByLog,
                s.Groups);
        });
        StatusBarSelection.Select(static s => (s.EventsLoading, s.ResolverStatus));
        FilterActiveSelection.Select(static s => s.IsFilteringEnabled);
        LensCountSelection.Select(static s => s.Lenses.Count);

        base.OnInitialized();
    }

    private static int TotalRawCount(
        LogView activeTable,
        ImmutableList<LogTabGroup> groups,
        (int Total, ImmutableDictionary<EventLogId, int> ByLog) rawCount)
    {
        if (activeTable.GroupId?.IsAll == true) { return rawCount.Total; }

        if (activeTable.GroupId is not { } groupId)
        {
            return rawCount.ByLog.GetValueOrDefault(activeTable.Id, 0);
        }

        var group = groups.FirstOrDefault(candidate => candidate.Id == groupId);

        return group is null ? 0 : group.MemberIds.Sum(id => rawCount.ByLog.GetValueOrDefault(id, 0));
    }
}
