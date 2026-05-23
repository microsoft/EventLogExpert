// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Frozen;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.LogTable;

internal sealed class ColumnDefaults : ILogTableColumnDefaultsProvider
{
    private static readonly ImmutableList<ColumnName> s_enabledColumns =
    [
        ColumnName.Level,
        ColumnName.DateAndTime,
        ColumnName.Source,
        ColumnName.EventId,
        ColumnName.TaskCategory
    ];

    private static readonly ImmutableList<ColumnName> s_order =
    [
        ColumnName.Level,
        ColumnName.DateAndTime,
        ColumnName.ActivityId,
        ColumnName.Log,
        ColumnName.ComputerName,
        ColumnName.Source,
        ColumnName.EventId,
        ColumnName.TaskCategory,
        ColumnName.Keywords,
        ColumnName.ProcessId,
        ColumnName.ThreadId,
        ColumnName.User
    ];

    private static readonly FrozenDictionary<ColumnName, int> s_widths = new Dictionary<ColumnName, int>
    {
        [ColumnName.Level] = 100,
        [ColumnName.DateAndTime] = 160,
        [ColumnName.ActivityId] = 270,
        [ColumnName.Log] = 100,
        [ColumnName.ComputerName] = 100,
        [ColumnName.Source] = 250,
        [ColumnName.EventId] = 80,
        [ColumnName.TaskCategory] = 180,
        [ColumnName.Keywords] = 100,
        [ColumnName.ProcessId] = 80,
        [ColumnName.ThreadId] = 80,
        [ColumnName.User] = 180
    }.ToFrozenDictionary();

    public ImmutableList<ColumnName> ColumnOrder => s_order;

    public FrozenDictionary<ColumnName, int> ColumnWidths => s_widths;

    public ImmutableList<ColumnName> EnabledColumns => s_enabledColumns;

    public int GetColumnWidth(ColumnName column) =>
        s_widths.GetValueOrDefault(column, 100);
}
