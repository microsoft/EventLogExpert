// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;

namespace EventLogExpert.Runtime.LogTable;

public interface ILogTableCommands
{
    /// <summary>Loads persisted column visibility, widths, and order into the LogTable store.</summary>
    void LoadColumns();

    /// <summary>Moves <paramref name="column" /> immediately before or after <paramref name="target" />.</summary>
    void ReorderColumn(ColumnName column, ColumnName target, bool insertAfter);

    /// <summary>Resets enabled columns, widths, and order to factory defaults.</summary>
    void ResetColumnDefaults();

    /// <summary>Activates the tab for <paramref name="logId" /> (focuses its rows in the table).</summary>
    void SetActiveTable(EventLogId logId);

    /// <summary>Collapses or expands every group at once, clearing per-group overrides.</summary>
    void SetAllGroupsCollapsed(bool collapsed);

    /// <summary>Sets the persisted width of <paramref name="column" /> to <paramref name="width" /> pixels.</summary>
    void SetColumnWidth(ColumnName column, int width);

    /// <summary>Groups rows by <paramref name="groupBy" /> (<see langword="null" /> clears grouping).</summary>
    void SetGroupBy(ColumnName? groupBy);

    /// <summary>Sets the current sort column to <paramref name="orderBy" /> (<see langword="null" /> clears the sort).</summary>
    void SetOrderBy(ColumnName? orderBy);

    /// <summary>Toggles whether <paramref name="column" /> is visible in the log table.</summary>
    void ToggleColumn(ColumnName column);

    /// <summary>Toggles the collapsed state of the group identified by <paramref name="groupKey" />.</summary>
    void ToggleGroupCollapsed(string groupKey);

    /// <summary>Flips the order direction of the group headers when grouping is active.</summary>
    void ToggleGroupSortDirection();

    /// <summary>Flips the current sort direction on the active sort column.</summary>
    void ToggleSortDirection();
}
