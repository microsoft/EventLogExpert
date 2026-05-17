// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Runtime;
using System.Runtime.ExceptionServices;

namespace EventLogExpert.Runtime.Filters;

internal sealed class FilterService : IFilterService
{
    /// <summary>Outer parallelism only kicks in when the combined work justifies the scheduling overhead.</summary>
    private const int OuterParallelTotalEventThreshold = 10_000;

    public IReadOnlyDictionary<EventLogId, IReadOnlyList<ResolvedEvent>> FilterActiveLogs(
        IEnumerable<EventLogData> logData,
        Filter filter)
    {
        var logs = logData as IReadOnlyList<EventLogData> ?? [.. logData];

        // Single log, no filters, or trivial total work: sequential per-log (inner PLINQ still
        // engages for >=10k events on a single large log).
        if (logs.Count <= 1 ||
            !filter.IsFilteringEnabled ||
            !ShouldParallelizeAcrossLogs(logs))
        {
            return BuildSequentialResult(logs, filter);
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
                    results[index] = FilterEventsSequential(logs[index].Events, filter);
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
        Filter filter)
    {
        if (!filter.IsFilteringEnabled)
        {
            return events as IReadOnlyList<ResolvedEvent> ?? [.. events];
        }

        // PLINQ scheduling overhead exceeds the benefit below this threshold.
        if (events is IReadOnlyCollection<ResolvedEvent> { Count: < 10_000 } collection)
        {
            return FilterEventsSequential(collection, filter);
        }

        return events.AsParallel()
            .Where(e => e.MatchesDateFilter(filter.DateFilter) &&
                e.MatchesFilters(filter.Filters))
            .ToList()
            .AsReadOnly();
    }

    public bool TryFormat(BasicFilter basicFilter, out string comparison) =>
        BasicFilterFormatter.TryFormat(basicFilter, out comparison);

    private static IReadOnlyList<ResolvedEvent> FilterEventsSequential(
        IEnumerable<ResolvedEvent> events,
        Filter filter) =>
        events
            .Where(e => e.MatchesDateFilter(filter.DateFilter) &&
                e.MatchesFilters(filter.Filters))
            .ToList()
            .AsReadOnly();

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

    private Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>> BuildSequentialResult(
        IReadOnlyList<EventLogData> logs,
        Filter filter)
    {
        var filtered = new Dictionary<EventLogId, IReadOnlyList<ResolvedEvent>>(logs.Count);

        foreach (var data in logs)
        {
            filtered.Add(data.Id, GetFilteredEvents(data.Events, filter));
        }

        return filtered;
    }
}
