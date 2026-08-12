// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;

namespace EventLogExpert.Runtime.LogTable;

public interface ILogTableQueries
{
    IReadOnlyList<LogTabGroup> GetTabGroups();

    bool HasActiveLogs();

    bool HasMultipleIndividualTabs();

    bool HasOtherTabsInGroup(LogTabGroupId groupId, EventLogId keepTabId);

    bool HasTabGroup(LogTabGroupId groupId);

    bool IsGroupDescending();

    bool IsGrouping();

    bool IsTabOpen(EventLogId tabId);

    bool IsUngroupedTabOpen(EventLogId tabId);
}
