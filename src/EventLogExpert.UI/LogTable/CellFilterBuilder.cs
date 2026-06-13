// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.LogTable;
using System.Globalization;
using FilterMode = EventLogExpert.Filtering.Evaluation.FilterMode;

namespace EventLogExpert.UI.LogTable;

internal static class CellFilterBuilder
{
    public static EventProperty? MapColumn(ColumnName column) =>
        column switch
        {
            ColumnName.EventId => EventProperty.Id,
            ColumnName.ActivityId => EventProperty.ActivityId,
            ColumnName.Level => EventProperty.Level,
            ColumnName.Keywords => EventProperty.Keywords,
            ColumnName.Source => EventProperty.Source,
            ColumnName.TaskCategory => EventProperty.TaskCategory,
            ColumnName.ProcessId => EventProperty.ProcessId,
            ColumnName.ThreadId => EventProperty.ThreadId,
            ColumnName.User => EventProperty.UserId,
            _ => null
        };

    public static bool TryBuild(ResolvedEvent @event, EventProperty property, bool exclude, out SavedFilter filter)
    {
        filter = SavedFilter.Empty;

        if (BuildComparison(@event, property) is not { } comparison) { return false; }

        var basicFilter = new BasicFilter(comparison, []);

        if (!BasicFilterFormatter.TryFormat(basicFilter, out var comparisonText)) { return false; }

        var saved = SavedFilter.TryCreate(
            comparisonText,
            basicFilter,
            isExcluded: exclude,
            isEnabled: true,
            mode: FilterMode.Basic);

        if (saved is null) { return false; }

        filter = saved;

        return true;
    }

    public static bool TryGetDisplayValue(ResolvedEvent @event, EventProperty property, out string value)
    {
        value = property switch
        {
            EventProperty.Id => @event.Id.ToString(CultureInfo.InvariantCulture),
            EventProperty.ActivityId => @event.ActivityId?.ToString() ?? string.Empty,
            EventProperty.Level => @event.Level,
            EventProperty.Keywords => @event.KeywordsDisplayName,
            EventProperty.Source => @event.Source,
            EventProperty.TaskCategory => @event.TaskCategory,
            EventProperty.ProcessId => @event.ProcessId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            EventProperty.ThreadId => @event.ThreadId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            EventProperty.UserId => @event.UserId?.Value ?? string.Empty,
            _ => string.Empty
        };

        return !string.IsNullOrWhiteSpace(value);
    }

    private static FilterComparison? BuildComparison(ResolvedEvent @event, EventProperty property)
    {
        if (property == EventProperty.Keywords)
        {
            return @event.Keywords.Count switch
            {
                0 => null,
                1 => new FilterComparison
                {
                    Property = EventProperty.Keywords,
                    Operator = ComparisonOperator.Equals,
                    MatchMode = MatchMode.Single,
                    Value = @event.Keywords[0]
                },
                _ => new FilterComparison
                {
                    Property = EventProperty.Keywords,
                    Operator = ComparisonOperator.Equals,
                    MatchMode = MatchMode.Many,
                    Values = [.. @event.Keywords]
                }
            };
        }

        if (!TryGetDisplayValue(@event, property, out var value)) { return null; }

        return new FilterComparison
        {
            Property = property,
            Operator = ComparisonOperator.Equals,
            MatchMode = MatchMode.Single,
            Value = value
        };
    }
}
