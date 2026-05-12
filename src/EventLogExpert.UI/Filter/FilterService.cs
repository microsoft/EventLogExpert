// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.UI.EventLog;
using System.Linq.Dynamic.Core;
using System.Runtime.ExceptionServices;
using System.Text;

namespace EventLogExpert.UI.Filter;

public sealed class FilterService : IFilterService
{
    /// <summary>Outer parallelism only kicks in when the combined work justifies the scheduling overhead.</summary>
    private const int OuterParallelTotalEventThreshold = 10_000;

    public IReadOnlyDictionary<EventLogId, IReadOnlyList<ResolvedEvent>> FilterActiveLogs(
        IEnumerable<EventLogData> logData,
        EventFilter eventFilter)
    {
        var logs = logData as IReadOnlyList<EventLogData> ?? [.. logData];

        // Single log, no filters, or trivial total work: sequential per-log (inner PLINQ still
        // engages for >=10k events on a single large log).
        if (logs.Count <= 1 ||
            !eventFilter.IsFilteringEnabled ||
            !ShouldParallelizeAcrossLogs(logs))
        {
            return BuildSequentialResult(logs, eventFilter);
        }

        // Multi-log heavy work: parallelize across logs, sequential within each log to avoid
        // oversubscribing the thread pool.
        var results = new IReadOnlyList<ResolvedEvent>[logs.Count];

        try
        {
            Parallel.For(0,
                logs.Count,
                index =>
                {
                    results[index] = FilterEventsSequential(logs[index].Events, eventFilter);
                });
        }
        catch (AggregateException aggregate) when (aggregate.InnerExceptions.Count == 1)
        {
            // Preserve the single-exception shape callers (and tests) expect.
            ExceptionDispatchInfo.Capture(aggregate.InnerExceptions[0]).Throw();
        }

        var filtered = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>(logs.Count);

        for (var index = 0; index < logs.Count; index++)
        {
            // Add() (not the indexer) preserves duplicate-key failure parity with the sequential path.
            filtered.Add(logs[index].Id, results[index]);
        }

        return filtered;
    }

    public IReadOnlyList<ResolvedEvent> GetFilteredEvents(
        IEnumerable<ResolvedEvent> events,
        EventFilter eventFilter)
    {
        if (!eventFilter.IsFilteringEnabled)
        {
            return events as IReadOnlyList<ResolvedEvent> ?? [.. events];
        }

        // PLINQ scheduling overhead exceeds the benefit below this threshold.
        if (events is IReadOnlyCollection<ResolvedEvent> { Count: < 10_000 } collection)
        {
            return FilterEventsSequential(collection, eventFilter);
        }

        return events.AsParallel()
            .Where(e => e.FilterByDate(eventFilter.DateFilter)
                .Filter(eventFilter.Filters))
            .ToList()
            .AsReadOnly();
    }

    public bool TryParse(BasicFilter basicFilter, out string comparison)
    {
        ArgumentNullException.ThrowIfNull(basicFilter);

        comparison = string.Empty;

        if (!TryFormatCondition(basicFilter.Comparison, null, out var comparisonText))
        {
            return false;
        }

        StringBuilder stringBuilder = new(comparisonText);

        foreach (var subFilter in basicFilter.SubFilters)
        {
            var joinPrefix = subFilter.JoinWithAny ? " || " : " && ";

            if (TryFormatCondition(subFilter.Data, joinPrefix, out var subText))
            {
                stringBuilder.AppendLine(subText);
            }
        }

        comparison = stringBuilder.ToString();

        return true;
    }

    public bool TryParseExpression(string? expression, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrEmpty(expression)) { return false; }

        try
        {
            _ = Enumerable.Empty<ResolvedEvent>().AsQueryable()
                .Where(ParsingConfig.Default, expression);

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;

            return false;
        }
    }

    private static string EscapeStringLiteral(string? value)
    {
        if (string.IsNullOrEmpty(value)) { return string.Empty; }

        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            switch (character)
            {
                case '\\': builder.Append("\\\\"); break;
                case '"': builder.Append("\\\""); break;
                case '\r': builder.Append("\\r"); break;
                case '\n': builder.Append("\\n"); break;
                case '\t': builder.Append("\\t"); break;
                default: builder.Append(character); break;
            }
        }

        return builder.ToString();
    }

    private static IReadOnlyList<ResolvedEvent> FilterEventsSequential(
        IEnumerable<ResolvedEvent> events,
        EventFilter eventFilter) =>
        events
            .Where(e => e.FilterByDate(eventFilter.DateFilter)
                .Filter(eventFilter.Filters))
            .ToList()
            .AsReadOnly();

    private static string GetComparisonString(FilterCategory type, FilterEvaluator evaluator) =>
        evaluator switch
        {
            FilterEvaluator.Equals => type switch
            {
                FilterCategory.Keywords => $"{type}.Any(e => string.Equals(e, ",
                FilterCategory.UserId => $"{type} != null && {type}.Value == ",
                _ => $"{type} == "
            },
            FilterEvaluator.Contains => type switch
            {
                FilterCategory.Id or FilterCategory.ActivityId => $"{type}.ToString().Contains",
                FilterCategory.Keywords => $"{type}.Any(e => e.Contains",
                FilterCategory.UserId => $"{type} != null && {type}.Value.Contains",
                _ => $"{type}.Contains"
            },
            FilterEvaluator.NotEqual => type switch
            {
                FilterCategory.Keywords => $"!{type}.Any(e => string.Equals(e, ",
                FilterCategory.UserId => $"{type} != null && {type}.Value != ",
                _ => $"{type} != ",
            },
            FilterEvaluator.NotContains => type switch
            {
                FilterCategory.Id or FilterCategory.ActivityId => $"!{type}.ToString().Contains",
                FilterCategory.Keywords => $"!{type}.Any(e => e.Contains",
                FilterCategory.UserId => $"{type} != null && !{type}.Value.Contains",
                _ => $"!{type}.Contains"
            },
            FilterEvaluator.MultiSelect => type switch
            {
                FilterCategory.Id or FilterCategory.Level => $"{type}.ToString())",
                FilterCategory.Keywords => $"{type}.Any",
                _ => $"{type})"
            },
            _ => string.Empty
        };

    private static bool ShouldParallelizeAcrossLogs(IReadOnlyList<EventLogData> logs)
    {
        long totalEvents = 0;

        foreach (var data in logs)
        {
            // If we can't cheaply size a log, assume the work is non-trivial and opt in to parallelism.
            if (data.Events is not IReadOnlyCollection<ResolvedEvent> collection)
            {
                return true;
            }

            totalEvents += collection.Count;

            if (totalEvents >= OuterParallelTotalEventThreshold)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryFormatCondition(FilterCondition condition, string? joinPrefix, out string formatted)
    {
        formatted = string.Empty;

        if (string.IsNullOrWhiteSpace(condition.Value) &&
            condition.Evaluator != FilterEvaluator.MultiSelect) { return false; }

        if (condition.Values.Count <= 0 &&
            condition.Evaluator == FilterEvaluator.MultiSelect) { return false; }

        StringBuilder stringBuilder = new(joinPrefix ?? string.Empty);

        if (condition.Evaluator != FilterEvaluator.MultiSelect ||
            condition.Category is FilterCategory.Keywords)
        {
            stringBuilder.Append(GetComparisonString(condition.Category, condition.Evaluator));
        }

        switch (condition.Evaluator)
        {
            case FilterEvaluator.Equals:
            case FilterEvaluator.NotEqual:
                if (condition.Category is FilterCategory.Keywords)
                {
                    stringBuilder.Append($"\"{EscapeStringLiteral(condition.Value)}\", StringComparison.OrdinalIgnoreCase))");
                }
                else
                {
                    stringBuilder.Append($"\"{EscapeStringLiteral(condition.Value)}\"");
                }

                break;
            case FilterEvaluator.Contains:
            case FilterEvaluator.NotContains:
                if (condition.Category is FilterCategory.Keywords)
                {
                    stringBuilder.Append($"(\"{EscapeStringLiteral(condition.Value)}\", StringComparison.OrdinalIgnoreCase))");
                }
                else
                {
                    stringBuilder.Append($"(\"{EscapeStringLiteral(condition.Value)}\", StringComparison.OrdinalIgnoreCase)");
                }

                break;
            case FilterEvaluator.MultiSelect:
                if (condition.Category is FilterCategory.Keywords)
                {
                    stringBuilder.Append($"(e => (new[] {{\"{string.Join("\", \"", condition.Values.Select(EscapeStringLiteral))}\"}}).Contains(e))");
                }
                else
                {
                    stringBuilder.Append($"(new[] {{\"{string.Join("\", \"", condition.Values.Select(EscapeStringLiteral))}\"}}).Contains(");
                }

                break;
            default: return false;
        }

        if (condition is { Evaluator: FilterEvaluator.MultiSelect, Category: not FilterCategory.Keywords })
        {
            stringBuilder.Append(GetComparisonString(condition.Category, condition.Evaluator));
        }

        formatted = stringBuilder.ToString();

        return true;
    }

    private Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>> BuildSequentialResult(
        IReadOnlyList<EventLogData> logs,
        EventFilter eventFilter)
    {
        var filtered = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>(logs.Count);

        foreach (var data in logs)
        {
            filtered.Add(data.Id, GetFilteredEvents(data.Events, eventFilter));
        }

        return filtered;
    }
}
