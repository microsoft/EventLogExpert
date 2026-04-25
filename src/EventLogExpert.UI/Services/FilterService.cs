// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using System.Linq.Dynamic.Core;
using System.Runtime.ExceptionServices;
using System.Text;

namespace EventLogExpert.UI.Services;

public sealed class FilterService : IFilterService
{
    /// <summary>Outer parallelism only kicks in when the combined work justifies the scheduling overhead.</summary>
    private const int OuterParallelTotalEventThreshold = 10_000;

    public IReadOnlyDictionary<EventLogId, IReadOnlyList<DisplayEventModel>> FilterActiveLogs(
        IEnumerable<EventLogData> logData,
        EventFilter eventFilter)
    {
        var logs = logData as IReadOnlyList<EventLogData> ?? [.. logData];

        // Single log, no filters, or trivial total work: sequential per-log (inner PLINQ still
        // engages for >=10k events on a single large log).
        if (logs.Count <= 1 ||
            !FilterMethods.IsFilteringEnabled(eventFilter) ||
            !ShouldParallelizeAcrossLogs(logs))
        {
            return BuildSequentialResult(logs, eventFilter);
        }

        // Multi-log heavy work: parallelize across logs, sequential within each log to avoid
        // oversubscribing the thread pool.
        var results = new IReadOnlyList<DisplayEventModel>[logs.Count];

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

        var filtered = new Dictionary<EventLogId, IReadOnlyList<DisplayEventModel>>(logs.Count);

        for (var index = 0; index < logs.Count; index++)
        {
            // Add() (not the indexer) preserves duplicate-key failure parity with the sequential path.
            filtered.Add(logs[index].Id, results[index]);
        }

        return filtered;
    }

    public IReadOnlyList<DisplayEventModel> GetFilteredEvents(
        IEnumerable<DisplayEventModel> events,
        EventFilter eventFilter)
    {
        if (!FilterMethods.IsFilteringEnabled(eventFilter))
        {
            return events as IReadOnlyList<DisplayEventModel> ?? [.. events];
        }

        // PLINQ scheduling overhead exceeds the benefit below this threshold.
        if (events is IReadOnlyCollection<DisplayEventModel> { Count: < 10_000 } collection)
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

        if (!TryFormatData(basicFilter.Comparison, null, out var comparisonText))
        {
            return false;
        }

        StringBuilder stringBuilder = new(comparisonText);

        foreach (var subFilter in basicFilter.SubFilters)
        {
            var joinPrefix = subFilter.JoinWithAny ? " || " : " && ";

            if (TryFormatData(subFilter.Data, joinPrefix, out var subText))
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
            _ = Enumerable.Empty<DisplayEventModel>().AsQueryable()
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

    private static IReadOnlyList<DisplayEventModel> FilterEventsSequential(
        IEnumerable<DisplayEventModel> events,
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
            if (data.Events is not IReadOnlyCollection<DisplayEventModel> collection)
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

    private static bool TryFormatData(FilterData data, string? joinPrefix, out string formatted)
    {
        formatted = string.Empty;

        if (string.IsNullOrWhiteSpace(data.Value) &&
            data.Evaluator != FilterEvaluator.MultiSelect) { return false; }

        if (data.Values.Count <= 0 &&
            data.Evaluator == FilterEvaluator.MultiSelect) { return false; }

        StringBuilder stringBuilder = new(joinPrefix ?? string.Empty);

        if (data.Evaluator != FilterEvaluator.MultiSelect ||
            data.Category is FilterCategory.Keywords)
        {
            stringBuilder.Append(GetComparisonString(data.Category, data.Evaluator));
        }

        switch (data.Evaluator)
        {
            case FilterEvaluator.Equals:
            case FilterEvaluator.NotEqual:
                if (data.Category is FilterCategory.Keywords)
                {
                    stringBuilder.Append($"\"{EscapeStringLiteral(data.Value)}\", StringComparison.OrdinalIgnoreCase))");
                }
                else
                {
                    stringBuilder.Append($"\"{EscapeStringLiteral(data.Value)}\"");
                }

                break;
            case FilterEvaluator.Contains:
            case FilterEvaluator.NotContains:
                if (data.Category is FilterCategory.Keywords)
                {
                    stringBuilder.Append($"(\"{EscapeStringLiteral(data.Value)}\", StringComparison.OrdinalIgnoreCase))");
                }
                else
                {
                    stringBuilder.Append($"(\"{EscapeStringLiteral(data.Value)}\", StringComparison.OrdinalIgnoreCase)");
                }

                break;
            case FilterEvaluator.MultiSelect:
                if (data.Category is FilterCategory.Keywords)
                {
                    stringBuilder.Append($"(e => (new[] {{\"{string.Join("\", \"", data.Values.Select(EscapeStringLiteral))}\"}}).Contains(e))");
                }
                else
                {
                    stringBuilder.Append($"(new[] {{\"{string.Join("\", \"", data.Values.Select(EscapeStringLiteral))}\"}}).Contains(");
                }

                break;
            default: return false;
        }

        if (data is { Evaluator: FilterEvaluator.MultiSelect, Category: not FilterCategory.Keywords })
        {
            stringBuilder.Append(GetComparisonString(data.Category, data.Evaluator));
        }

        formatted = stringBuilder.ToString();

        return true;
    }

    private Dictionary<EventLogId, IReadOnlyList<DisplayEventModel>> BuildSequentialResult(
        IReadOnlyList<EventLogData> logs,
        EventFilter eventFilter)
    {
        var filtered = new Dictionary<EventLogId, IReadOnlyList<DisplayEventModel>>(logs.Count);

        foreach (var data in logs)
        {
            filtered.Add(data.Id, GetFilteredEvents(data.Events, eventFilter));
        }

        return filtered;
    }
}
