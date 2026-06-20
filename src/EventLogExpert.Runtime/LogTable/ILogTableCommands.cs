// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;

namespace EventLogExpert.Runtime.LogTable;

public interface ILogTableCommands
{
    void CloseAllButThis(EventLogId tabId);

    void CloseGroup(LogTabGroupId groupId);

    void CloseOthersInGroup(LogTabGroupId groupId, EventLogId keepTabId);

    void LoadColumns();

    void MoveTabToGroup(EventLogId tabId, LogTabGroupId targetGroupId);

    void NewGroupFromTab(EventLogId tabId, string groupName);

    void RemoveTabFromGroup(EventLogId tabId);

    void RenameGroup(LogTabGroupId groupId, string newName);

    void ReorderColumn(ColumnName column, ColumnName target, bool insertAfter);

    void ResetColumnDefaults();

    void SetActiveTable(EventLogId logId);

    void SetAllGroupsCollapsed(bool collapsed);

    void SetColumnWidth(ColumnName column, int width);

    void SetGroupBy(ColumnName? groupBy);

    void SetOrderBy(ColumnName? orderBy);

    void SetTabGroupCollapsed(LogTabGroupId groupId, bool collapsed);

    void ToggleColumn(ColumnName column);

    void ToggleGroupCollapsed(string groupKey);

    void ToggleGroupSortDirection();

    void ToggleSortDirection();
}
