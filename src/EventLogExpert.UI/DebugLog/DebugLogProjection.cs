// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Common;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.UI.DebugLog;

public static class DebugLogProjection
{
    public static (List<string> Lines, int MatchedEntryCount) Project(
        IReadOnlyList<DebugLogEntry> entries,
        ComparisonOperator levelOperator,
        IReadOnlyList<LogLevel> levels,
        string? textFilter)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return ProjectRange(entries, 0, entries.Count, levelOperator, levels, textFilter);
    }

    public static (List<string> Lines, int MatchedEntryCount) ProjectRange(
        IReadOnlyList<DebugLogEntry> entries,
        int startIndex,
        int endIndex,
        ComparisonOperator levelOperator,
        IReadOnlyList<LogLevel> levels,
        string? textFilter)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(endIndex, entries.Count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(startIndex, endIndex);

        var sliceLength = endIndex - startIndex;
        var lines = new List<string>(sliceLength);
        var matchedCount = 0;

        var hasLevelFilter = levels.Count > 0;
        var hasTextFilter = !string.IsNullOrEmpty(textFilter);

        for (var i = startIndex; i < endIndex; i++)
        {
            var entry = entries[i];

            if (hasLevelFilter && !MatchesLevel(entry, levelOperator, levels)) { continue; }

            if (hasTextFilter &&
                entry.RawLine.IndexOf(textFilter!, entry.MessageStartIndex, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            matchedCount++;
            AppendPhysicalLinesReversed(entry.RawLine, lines);
        }

        return (lines, matchedCount);
    }

    private static void AppendPhysicalLinesReversed(string raw, List<string> lines)
    {
        if (raw.Length == 0)
        {
            lines.Add(string.Empty);

            return;
        }

        var split = raw.Split('\n');

        for (var i = split.Length - 1; i >= 0; i--)
        {
            lines.Add(split[i]);
        }
    }

    private static bool MatchesLevel(DebugLogEntry entry, ComparisonOperator op, IReadOnlyList<LogLevel> levels)
    {
        if (entry.Level is null) { return op == ComparisonOperator.NotEqual; }

        var contains = levels.Contains(entry.Level.Value);

        return op == ComparisonOperator.NotEqual ? !contains : contains;
    }
}
