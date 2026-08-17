// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Runtime.Common.Display;
using System.Globalization;

namespace EventLogExpert.Runtime.LogTable;

public static class ColumnDescriptors
{
    private static readonly ColumnDescriptor[] s_accessors = BuildAccessors();

    /// <summary>Renders the table cell / copy text for <paramref name="column" />.</summary>
    public static string GetCellText(ResolvedEvent @event, ColumnName column, ColumnFormatContext context) =>
        Get(column).CellText(@event, context);

    /// <summary>Gets the stored field a column reader projects for <paramref name="column" />.</summary>
    public static EventFieldId GetFieldId(ColumnName column) => Get(column).FieldId;

    /// <summary>Gets the cell-filter property for <paramref name="column" />, or null when it is not cell-filterable.</summary>
    public static EventProperty? GetFilterProperty(ColumnName column) => Get(column).FilterProperty;

    /// <summary>Renders the group-by header text for <paramref name="column" /> (Log = channel LogName, not the file).</summary>
    public static string GetGroupText(ResolvedEvent @event, ColumnName column, ColumnFormatContext context)
    {
        ColumnDescriptor accessor = Get(column);

        return (accessor.GroupText ?? accessor.CellText)(@event, context);
    }

    private static ColumnDescriptor[] BuildAccessors()
    {
        var accessors = new ColumnDescriptor[Enum.GetValues<ColumnName>().Length];

        accessors[(int)ColumnName.RecordId] =
            new(EventFieldId.RecordId, null, static (e, _) => e.RecordId?.ToString() ?? string.Empty);
        accessors[(int)ColumnName.Level] =
            new(EventFieldId.Level, EventProperty.Level, static (e, _) => e.Level);
        accessors[(int)ColumnName.DateAndTime] =
            new(EventFieldId.TimeCreated, null, static (e, ctx) => FormatTime(e.TimeCreated, ctx));
        accessors[(int)ColumnName.ActivityId] =
            new(EventFieldId.ActivityId, EventProperty.ActivityId, static (e, _) => e.ActivityId?.ToString() ?? string.Empty);
        accessors[(int)ColumnName.Log] =
            new(EventFieldId.LogName, null, static (e, _) => OwningLogDisplay.ShortName(e.OwningLog),
                static (e, _) => e.LogName);
        accessors[(int)ColumnName.ComputerName] =
            new(EventFieldId.ComputerName, null, static (e, _) => e.ComputerName);
        accessors[(int)ColumnName.Source] =
            new(EventFieldId.Source, EventProperty.Source, static (e, _) => e.Source);
        accessors[(int)ColumnName.EventId] =
            new(EventFieldId.Id, EventProperty.Id, static (e, _) => e.Id.ToString());
        accessors[(int)ColumnName.TaskCategory] =
            new(EventFieldId.TaskCategory, EventProperty.TaskCategory, static (e, _) => e.TaskCategory);
        accessors[(int)ColumnName.Keywords] =
            new(EventFieldId.KeywordsDisplay, EventProperty.Keywords, static (e, _) => e.KeywordsDisplayName);
        accessors[(int)ColumnName.ProcessId] =
            new(EventFieldId.ProcessId, EventProperty.ProcessId, static (e, _) => e.ProcessId?.ToString() ?? string.Empty);
        accessors[(int)ColumnName.ThreadId] =
            new(EventFieldId.ThreadId, EventProperty.ThreadId, static (e, _) => e.ThreadId?.ToString() ?? string.Empty);
        accessors[(int)ColumnName.User] =
            new(EventFieldId.UserDisplayName, EventProperty.UserDisplayName, static (e, _) => e.UserDisplayName);
        accessors[(int)ColumnName.Opcode] =
            new(EventFieldId.Opcode, EventProperty.Opcode, static (e, _) => e.Opcode);

        return accessors;
    }

    private static string FormatTime(DateTime timeCreated, ColumnFormatContext context)
    {
        DateTime converted = timeCreated.ConvertTimeZone(context.TimeZone);

        return context.DateTimeFormat is null ?
            converted.ToString() :
            converted.ToString(context.DateTimeFormat, CultureInfo.InvariantCulture);
    }

    private static ColumnDescriptor Get(ColumnName column)
    {
        int index = (int)column;

        if ((uint)index >= (uint)s_accessors.Length || s_accessors[index] is not { } accessor)
        {
            throw new ArgumentOutOfRangeException(nameof(column), column, "No accessor is registered for the column.");
        }

        return accessor;
    }
}
