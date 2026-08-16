// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.Common.Display;

namespace EventLogExpert.Runtime.LogTable;

public static class EventTableColumnFormatter
{
    public const string DescriptionColumnHeader = "Description";

    public static string GetCellText(
        ResolvedEvent @event, ColumnName column, TimeZoneInfo timeZone, string? dateTimeFormat = null) =>
        ColumnDescriptors.GetCellText(@event, column, new ColumnFormatContext(timeZone, dateTimeFormat));

    public static string GetColumnHeader(ColumnName column, TimeZoneInfo timeZone) =>
        column == ColumnName.DateAndTime && !timeZone.Equals(TimeZoneInfo.Local) ?
            $"Date and Time {timeZone.DisplayName.Split(' ').First()}" :
            column.ToFullString();
}
