// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Runtime.DebugLog;

public static class DebugLogProjection
{
    public static (List<string> Lines, int MatchedEntryCount) Project(
        IReadOnlyList<DebugLogEntry> entries,
        IReadOnlyList<DebugLogFilter> filters)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return ProjectRange(entries, 0, entries.Count, filters);
    }

    public static (List<string> Lines, int MatchedEntryCount) ProjectRange(
        IReadOnlyList<DebugLogEntry> entries,
        int startIndex,
        int endIndex,
        IReadOnlyList<DebugLogFilter> filters)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(endIndex, entries.Count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(startIndex, endIndex);

        var predicates = CompileFilters(filters);
        var lines = new List<string>(endIndex - startIndex);
        var matchedCount = 0;

        for (var i = startIndex; i < endIndex; i++)
        {
            var entry = entries[i];

            if (!Matches(entry, predicates)) { continue; }

            matchedCount++;
            AppendPhysicalLines(entry.RawLine, lines);
        }

        return (lines, matchedCount);
    }

    private static void AppendPhysicalLines(string raw, List<string> lines)
    {
        if (raw.Length == 0)
        {
            lines.Add(string.Empty);

            return;
        }

        var split = raw.Split('\n');

        foreach (var line in split)
        {
            lines.Add(line);
        }
    }

    // A null predicate makes a filter a no-op, so an incomplete (half-configured) or disabled row never hides entries.
    private static Func<DebugLogEntry, bool>? Compile(DebugLogFilter filter)
    {
        if (!filter.IsComplete || !filter.IsEnabled) { return null; }

        var excluded = filter.IsExcluded;
        var wantIn = filter.Operator != ComparisonOperator.NotEqual;

        switch (filter.Field)
        {
            case DebugLogFilterField.Level:
            {
                var set = ParseLevels(filter.Values);

                if (set.Count == 0) { return null; }

                return entry => Effective(wantIn == (entry.Level.HasValue && set.Contains(entry.Level.Value)), excluded);
            }

            case DebugLogFilterField.Process:
            {
                var set = ParseProcessOrigins(filter.Values);

                if (set.Count == 0) { return null; }

                return entry => Effective(wantIn == (entry.ProcessOrigin.HasValue && set.Contains(entry.ProcessOrigin.Value)), excluded);
            }

            case DebugLogFilterField.Category:
            {
                var set = new HashSet<string>(filter.Values, StringComparer.Ordinal);

                return entry => Effective(wantIn == set.Contains(entry.Category ?? string.Empty), excluded);
            }

            case DebugLogFilterField.Message:
            {
                var op = filter.Operator;
                var text = filter.Values[0];

                return entry => Effective(MatchesMessage(entry, op, text), excluded);
            }

            default:
                return null;
        }
    }

    private static List<Func<DebugLogEntry, bool>> CompileFilters(IReadOnlyList<DebugLogFilter> filters)
    {
        var predicates = new List<Func<DebugLogEntry, bool>>(filters.Count);

        foreach (var filter in filters)
        {
            var predicate = Compile(filter);

            if (predicate is not null) { predicates.Add(predicate); }
        }

        return predicates;
    }

    private static bool Effective(bool predicate, bool excluded) => excluded ? !predicate : predicate;

    private static bool Matches(DebugLogEntry entry, List<Func<DebugLogEntry, bool>> predicates)
    {
        foreach (var predicate in predicates)
        {
            if (!predicate(entry)) { return false; }
        }

        return true;
    }

    private static bool MatchesMessage(DebugLogEntry entry, ComparisonOperator op, string text) => op switch
    {
        ComparisonOperator.Contains =>
            entry.RawLine.IndexOf(text, entry.MessageStartIndex, StringComparison.OrdinalIgnoreCase) >= 0,
        ComparisonOperator.NotContains =>
            entry.RawLine.IndexOf(text, entry.MessageStartIndex, StringComparison.OrdinalIgnoreCase) < 0,
        ComparisonOperator.Equals =>
            entry.RawLine.AsSpan(entry.MessageStartIndex).Equals(text, StringComparison.OrdinalIgnoreCase),
        ComparisonOperator.NotEqual =>
            !entry.RawLine.AsSpan(entry.MessageStartIndex).Equals(text, StringComparison.OrdinalIgnoreCase),
        _ => true
    };

    private static HashSet<LogLevel> ParseLevels(IReadOnlyList<string> values)
    {
        var set = new HashSet<LogLevel>();

        foreach (var value in values)
        {
            if (Enum.TryParse<LogLevel>(value, out var level)) { set.Add(level); }
        }

        return set;
    }

    private static HashSet<ProcessOrigin> ParseProcessOrigins(IReadOnlyList<string> values)
    {
        var set = new HashSet<ProcessOrigin>();

        foreach (var value in values)
        {
            if (Enum.TryParse<ProcessOrigin>(value, out var origin)) { set.Add(origin); }
        }

        return set;
    }
}
