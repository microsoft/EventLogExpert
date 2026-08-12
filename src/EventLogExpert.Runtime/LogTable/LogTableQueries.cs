// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using Fluxor;

namespace EventLogExpert.Runtime.LogTable;

internal sealed class LogTableQueries(IState<LogTableState> logTableState) : ILogTableQueries
{
    private readonly IState<LogTableState> _logTableState = logTableState;

    public IReadOnlyList<LogTabGroup> GetTabGroups() => _logTableState.Value.Groups;

    public bool HasActiveLogs() => _logTableState.Value.EventTables.Any(table => !table.IsCombined);

    public bool HasMultipleIndividualTabs() =>
        _logTableState.Value.EventTables.Count(table => !table.IsCombined) > 1;

    public bool HasOtherTabsInGroup(LogTabGroupId groupId, EventLogId keepTabId)
    {
        var state = _logTableState.Value;

        if (state.Groups.FirstOrDefault(group => group.Id == groupId) is not { } group) { return false; }

        return group.MemberIds.Contains(keepTabId) &&
            state.EventTables.Count(table => table.GroupId is null && group.MemberIds.Contains(table.Id)) > 1;
    }

    public bool HasTabGroup(LogTabGroupId groupId) => _logTableState.Value.Groups.Any(group => group.Id == groupId);

    public bool IsGroupDescending() => _logTableState.Value.IsGroupDescending;

    public bool IsGrouping() => _logTableState.Value.GroupBy is not null;

    public bool IsTabOpen(EventLogId tabId) => _logTableState.Value.EventTables.Any(table => table.Id == tabId);

    public bool IsUngroupedTabOpen(EventLogId tabId) =>
        _logTableState.Value.EventTables.Any(table => table.Id == tabId && table.GroupId is null);
}
