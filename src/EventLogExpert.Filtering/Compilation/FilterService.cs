// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Structured;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using System.Collections.Immutable;
using System.Runtime.ExceptionServices;

namespace EventLogExpert.Filtering.Compilation;

public sealed class FilterService : IFilterService
{
    /// <summary>Outer parallelism only kicks in when the combined work justifies the scheduling overhead.</summary>
    private const int OuterParallelTotalEventThreshold = 10_000;

    public static byte[] ClassifyHighlightWinners(
        IEventColumnReader reader,
        IReadOnlyList<int> survivingOrder,
        IReadOnlyList<SavedFilter> orderedColoredFilters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(survivingOrder);
        ArgumentNullException.ThrowIfNull(orderedColoredFilters);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(orderedColoredFilters.Count, 31);

        ColumnCompiledFilter[] compiledFilters = CompileHighlightColumnFilters(orderedColoredFilters);
        byte[] winners = new byte[reader.Count];

        foreach (int index in survivingOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();

            EventLocator locator = reader.LocatorAt(index);

            for (int ordinal = 0; ordinal < compiledFilters.Length; ordinal++)
            {
                if (compiledFilters[ordinal].Evaluate(reader, locator) != FilterMatch.Match) { continue; }

                winners[index] = (byte)(ordinal + 1);

                break;
            }
        }

        return winners;
    }

    public static IReadOnlyList<int> GetSurvivingOrder(IEventColumnReader reader, Filter filter)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var count = reader.Count;

        if (!filter.IsFilteringEnabled)
        {
            var all = new int[count];

            for (var index = 0; index < count; index++) { all[index] = index; }

            return all;
        }

        var compiledFilters = CompileColumnFilters(filter.Filters);

        var dateFilter = filter.DateFilter;
        var dateEnabled = dateFilter?.IsEnabled is true;
        var after = dateFilter?.After;
        var before = dateFilter?.Before;

        var survivors = new List<int>();

        for (var index = 0; index < count; index++)
        {
            var locator = reader.LocatorAt(index);

            if (!MatchesCompiledFilters(reader, locator, compiledFilters)) { continue; }

            if (dateEnabled && !MatchesDateRange(reader, locator, after, before)) { continue; }

            survivors.Add(index);
        }

        return survivors;
    }

    public IReadOnlyDictionary<EventLogId, IReadOnlyList<ResolvedEvent>> FilterActiveLogs(
        IReadOnlyList<(EventLogId Id, IReadOnlyList<ResolvedEvent> Events)> logs,
        Filter filter)
    {
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

    private static List<(ColumnCompiledFilter Compiled, bool IsExcluded)> CompileColumnFilters(
        ImmutableList<SavedFilter> filters)
    {
        var compiledFilters = new List<(ColumnCompiledFilter Compiled, bool IsExcluded)>(filters.Count);

        foreach (var savedFilter in filters)
        {
            // A null AoS Compiled is skipped without touching the isEmpty/isFiltered decision. Skipping at compile time
            // is equivalent to the row oracle's per-event `continue` before the exclude/include arms.
            if (savedFilter.Compiled is null) { continue; }

            if (!FilterCompiler.TryCompileColumn(savedFilter.ComparisonText, out var columnCompiled, out var error))
            {
                throw new InvalidOperationException(
                    $"Filter '{savedFilter.ComparisonText}' has a non-null AoS {nameof(SavedFilter.Compiled)} but failed " +
                    $"column-direct compilation: {error}");
            }

            compiledFilters.Add((columnCompiled, savedFilter.IsExcluded));
        }

        return compiledFilters;
    }

    private static ColumnCompiledFilter[] CompileHighlightColumnFilters(IReadOnlyList<SavedFilter> filters)
    {
        var compiledFilters = new ColumnCompiledFilter[filters.Count];

        for (int index = 0; index < filters.Count; index++)
        {
            SavedFilter savedFilter = filters[index];

            if (savedFilter.Compiled is null)
            {
                throw new InvalidOperationException("Highlight classification requires a non-null compiled filter.");
            }

            if (!FilterCompiler.TryCompileColumn(savedFilter.ComparisonText, out var columnCompiled, out var error))
            {
                throw new InvalidOperationException(
                    $"Filter '{savedFilter.ComparisonText}' has a non-null AoS {nameof(SavedFilter.Compiled)} but failed " +
                    $"column-direct compilation: {error}");
            }

            compiledFilters[index] = columnCompiled;
        }

        return compiledFilters;
    }

    private static IReadOnlyList<ResolvedEvent> FilterEventsSequential(
        IEnumerable<ResolvedEvent> events,
        Filter filter) =>
        events
            .Where(e => e.MatchesDateFilter(filter.DateFilter) &&
                e.MatchesFilters(filter.Filters))
            .ToList()
            .AsReadOnly();

    private static bool MatchesCompiledFilters(
        IEventColumnReader reader,
        EventLocator locator,
        List<(ColumnCompiledFilter Compiled, bool IsExcluded)> compiledFilters)
    {
        var isEmpty = true;
        var isFiltered = false;

        foreach (var (compiled, isExcluded) in compiledFilters)
        {
            FilterMatch match = compiled.Evaluate(reader, locator);

            if (isExcluded)
            {
                // Exclude hides only on a decisive Match; Unknown and NoMatch keep the row visible.
                if (match == FilterMatch.Match) { return false; }

                continue;
            }

            isEmpty = false;

            // Include keeps the row on a Match OR an Unknown; only a decisive NoMatch fails to satisfy it.
            if (match != FilterMatch.NoMatch) { isFiltered = true; }
        }

        return isEmpty || isFiltered;
    }

    private static bool MatchesDateRange(
        IEventColumnReader reader,
        EventLocator locator,
        DateTime? after,
        DateTime? before)
    {
        reader.GetField(locator, EventFieldId.TimeCreated).TryGetDateTime(out var timeCreated);

        // Lifted nullable comparison mirrors the row oracle: a null After or Before makes the arm false.
        return timeCreated >= after && timeCreated <= before;
    }

    private static bool ShouldParallelizeAcrossLogs(
        IReadOnlyList<(EventLogId Id, IReadOnlyList<ResolvedEvent> Events)> logs)
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
        IReadOnlyList<(EventLogId Id, IReadOnlyList<ResolvedEvent> Events)> logs,
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
