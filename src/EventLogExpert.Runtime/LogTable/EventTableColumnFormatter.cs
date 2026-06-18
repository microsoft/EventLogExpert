// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.Common.Display;
using System.Globalization;

namespace EventLogExpert.Runtime.LogTable;

public static class EventTableColumnFormatter
{
    public const string DescriptionColumnHeader = "Description";

    public static string GetCellText(
        ResolvedEvent @event, ColumnName column, TimeZoneInfo timeZone, string? dateTimeFormat = null) =>
        column switch
        {
            ColumnName.RecordId => @event.RecordId?.ToString() ?? string.Empty,
            ColumnName.Level => @event.Level,
            ColumnName.DateAndTime => FormatTimeCreated(@event.TimeCreated, timeZone, dateTimeFormat),
            ColumnName.ActivityId => @event.ActivityId?.ToString() ?? string.Empty,
            ColumnName.Log => GetLogShortName(@event.OwningLog),
            ColumnName.ComputerName => @event.ComputerName,
            ColumnName.Source => @event.Source,
            ColumnName.EventId => @event.Id.ToString(),
            ColumnName.TaskCategory => @event.TaskCategory,
            ColumnName.Keywords => @event.KeywordsDisplayName,
            ColumnName.ProcessId => @event.ProcessId?.ToString() ?? string.Empty,
            ColumnName.ThreadId => @event.ThreadId?.ToString() ?? string.Empty,
            ColumnName.User => @event.UserId?.ToString() ?? string.Empty,
            _ => string.Empty
        };

    public static string GetColumnHeader(ColumnName column, TimeZoneInfo timeZone) =>
        column == ColumnName.DateAndTime && !timeZone.Equals(TimeZoneInfo.Local)
            ? $"Date and Time {timeZone.DisplayName.Split(' ').First()}"
            : column.ToFullString();

    private static string FormatTimeCreated(DateTime timeCreated, TimeZoneInfo timeZone, string? dateTimeFormat)
    {
        DateTime converted = timeCreated.ConvertTimeZone(timeZone);

        return dateTimeFormat is null
            ? converted.ToString()
            : converted.ToString(dateTimeFormat, CultureInfo.InvariantCulture);
    }

    private static string GetLogShortName(string owningLog) =>
        owningLog[(owningLog.LastIndexOf('\\') + 1)..];
}
