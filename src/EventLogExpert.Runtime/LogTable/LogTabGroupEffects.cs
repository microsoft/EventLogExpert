// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Runtime.EventLog;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.LogTable;

internal sealed class LogTabGroupEffects(IState<LogTableState> logTableState, IEventLogCommands eventLogCommands)
{
    private readonly IEventLogCommands _eventLogCommands = eventLogCommands;
    private readonly IState<LogTableState> _logTableState = logTableState;

    [EffectMethod]
    public Task HandleCloseAllButThis(CloseAllButThisAction action, IDispatcher dispatcher)
    {
        var state = _logTableState.Value;
        var anchor = state.EventTables.FirstOrDefault(table => table.Id == action.TabId);

        if (anchor is null || anchor.IsCombined) { return Task.CompletedTask; }

        CloseTabs(state.EventTables.Where(table => !table.IsCombined && table.Id != action.TabId).ToList());

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleCloseGroup(CloseGroupAction action, IDispatcher dispatcher)
    {
        var state = _logTableState.Value;
        var group = state.Groups.FirstOrDefault(candidate => candidate.Id == action.GroupId);

        if (group is null) { return Task.CompletedTask; }

        CloseTabs(MemberTabs(state, group.MemberIds));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleCloseOthersInGroup(CloseOthersInGroupAction action, IDispatcher dispatcher)
    {
        var state = _logTableState.Value;
        var group = state.Groups.FirstOrDefault(candidate => candidate.Id == action.GroupId);

        if (group is null || !group.MemberIds.Contains(action.KeepTabId)) { return Task.CompletedTask; }

        CloseTabs(MemberTabs(state, group.MemberIds.Remove(action.KeepTabId)));

        return Task.CompletedTask;
    }

    private static IReadOnlyList<LogView> MemberTabs(LogTableState state, ImmutableHashSet<EventLogId> memberIds)
    {
        var tabs = new List<LogView>(memberIds.Count);

        foreach (var memberId in memberIds)
        {
            var tab = state.EventTables.FirstOrDefault(table => table.Id == memberId);

            if (tab is not null) { tabs.Add(tab); }
        }

        return tabs;
    }

    private void CloseTabs(IReadOnlyList<LogView> tabs)
    {
        foreach (var tab in tabs)
        {
            _eventLogCommands.CloseLog(tab.Id, tab.LogName);
        }
    }
}
