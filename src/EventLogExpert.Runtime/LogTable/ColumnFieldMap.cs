// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.LogTable;

/// <summary>Maps a display <see cref="ColumnName" /> to the <see cref="EventFieldId" /> a column reader projects for it.</summary>
internal static class ColumnFieldMap
{
    internal static EventFieldId ToFieldId(ColumnName column) =>
        column switch
        {
            ColumnName.RecordId => EventFieldId.RecordId,
            ColumnName.Level => EventFieldId.Level,
            ColumnName.DateAndTime => EventFieldId.TimeCreated,
            ColumnName.ActivityId => EventFieldId.ActivityId,
            ColumnName.Log => EventFieldId.LogName,
            ColumnName.ComputerName => EventFieldId.ComputerName,
            ColumnName.Source => EventFieldId.Source,
            ColumnName.EventId => EventFieldId.Id,
            ColumnName.TaskCategory => EventFieldId.TaskCategory,
            ColumnName.Keywords => EventFieldId.KeywordsDisplay,
            ColumnName.ProcessId => EventFieldId.ProcessId,
            ColumnName.ThreadId => EventFieldId.ThreadId,
            ColumnName.User => EventFieldId.UserId,
            _ => throw new ArgumentOutOfRangeException(nameof(column), column, "No field mapping for column.")
        };
}
