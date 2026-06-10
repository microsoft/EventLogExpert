// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using System.Globalization;

namespace EventLogExpert.Runtime.LogTable;

/// <summary>Per-column grouping key mirroring the field each grouped comparer reads; "" is the no-value bucket.</summary>
internal static class ResolvedEventGroupKey
{
    public static string For(ColumnName column, ResolvedEvent @event) =>
        column switch
        {
            ColumnName.Level => @event.Level,
            ColumnName.DateAndTime => @event.TimeCreated.Ticks.ToString(CultureInfo.InvariantCulture),
            ColumnName.ActivityId => @event.ActivityId?.ToString("D", CultureInfo.InvariantCulture) ?? string.Empty,
            ColumnName.Log => @event.LogName,
            ColumnName.ComputerName => @event.ComputerName,
            ColumnName.Source => @event.Source,
            ColumnName.EventId => @event.Id.ToString(CultureInfo.InvariantCulture),
            ColumnName.TaskCategory => @event.TaskCategory,
            ColumnName.Keywords => @event.KeywordsDisplayName,
            ColumnName.ProcessId => @event.ProcessId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ColumnName.ThreadId => @event.ThreadId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ColumnName.User => @event.UserId?.Value ?? string.Empty,
            _ => string.Empty
        };
}
