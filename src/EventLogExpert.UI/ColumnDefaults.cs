// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Frozen;
using System.Collections.Immutable;

namespace EventLogExpert.UI;

public static class ColumnDefaults
{
    public static readonly ImmutableList<ColumnName> EnabledColumns =
    [
        ColumnName.Level,
        ColumnName.DateAndTime,
        ColumnName.Source,
        ColumnName.EventId,
        ColumnName.TaskCategory
    ];

    public static readonly ImmutableList<ColumnName> Order =
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

    public static readonly FrozenDictionary<ColumnName, int> Widths = new Dictionary<ColumnName, int>
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

    public static int GetWidth(ColumnName column) =>
        Widths.TryGetValue(column, out int width) ? width : 100;
}
