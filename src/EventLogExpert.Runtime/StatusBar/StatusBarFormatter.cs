// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Memory;

namespace EventLogExpert.Runtime.StatusBar;

public static class StatusBarFormatter
{
    public static string? FilterIndicatorTooltip(bool persistentActive, int lensCount)
    {
        var lensText = lensCount switch
        {
            <= 0 => null,
            1 => "1 lens",
            _ => $"{lensCount} lenses"
        };

        return (persistentActive, lensText) switch
        {
            (true, null) => "Filter active",
            (true, not null) => $"Filter + {lensText}",
            (false, not null) => lensText,
            (false, null) => null
        };
    }

    public static string FormatActivityAnnouncement(
        bool isLoading,
        bool bufferFull,
        bool continuouslyUpdating,
        string resolverStatus,
        DisplayIndicatorKind displayIndicator = DisplayIndicatorKind.None)
    {
        if (!string.IsNullOrEmpty(resolverStatus)) { return resolverStatus; }

        if (displayIndicator == DisplayIndicatorKind.Fault) { return "These events could not be prepared"; }

        if (bufferFull) { return "Buffer full"; }

        if (isLoading) { return "Loading"; }

        return displayIndicator switch
        {
            DisplayIndicatorKind.EmptyPending => "Loading events",
            DisplayIndicatorKind.ReorderPending => "Reordering events",
            _ => continuouslyUpdating ? "Continuously updating" : string.Empty
        };
    }

    public static string FormatCounts(int total, int shown, bool isFiltered, int selectedCount)
    {
        var head = isFiltered ? $"{shown:N0} of {total:N0} shown" : $"{total:N0} events";

        return selectedCount >= 2 ? $"{head} \u00b7 {selectedCount:N0} selected" : head;
    }

    public static string? FormatMemory(long usedBytes, long budgetBytes) =>
        budgetBytes is <= 0 or long.MaxValue ?
            null :
            $"Memory: {FormatBytes(usedBytes)} / {FormatBytes(budgetBytes)}";

    public static string? FormatMemoryStatus(MemoryPressureLevel level, int partiallyLoadedCount, string? heaviestLogName)
    {
        string? pressure = level switch
        {
            MemoryPressureLevel.Paused => heaviestLogName is null ?
                "Live updates paused (memory)" :
                $"Live updates paused (memory); close {heaviestLogName} to recover the most",
            MemoryPressureLevel.Warning => heaviestLogName is null ?
                "Memory pressure high" :
                $"Memory pressure high; close {heaviestLogName} to recover the most",
            _ => null
        };

        string? partial = partiallyLoadedCount switch
        {
            <= 0 => null,
            1 => "1 log partially loaded (memory); reopen to load fully",
            _ => $"{partiallyLoadedCount} logs partially loaded (memory); reopen to load fully"
        };

        return (pressure, partial) switch
        {
            (null, null) => null,
            (not null, null) => pressure,
            (null, not null) => partial,
            _ => $"{pressure} \u00b7 {partial}"
        };
    }

    public static string FormatSource(
        LogView? active,
        IReadOnlyList<LogView> eventTables,
        IReadOnlyList<LogTabGroup> groups)
    {
        if (active is null) { return "No log open"; }

        if (active.GroupId is not { } groupId)
        {
            return active.FileName is { } fileName ? Path.GetFileName(fileName) : active.LogName;
        }

        if (groupId.IsAll)
        {
            var openLogs = 0;

            foreach (var table in eventTables)
            {
                if (!table.IsCombined) { openLogs++; }
            }

            return $"All logs ({openLogs})";
        }

        var group = groups.FirstOrDefault(candidate => candidate.Id == groupId);

        if (group is null) { return "Combined"; }

        return string.IsNullOrEmpty(group.Name) ? $"Combined ({group.MemberIds.Count} logs)" : group.Name;
    }

    private static string FormatBytes(long bytes)
    {
        const long OneKibibyte = 1024;
        const long OneMebibyte = OneKibibyte * 1024;
        const long OneGibibyte = OneMebibyte * 1024;

        return bytes switch
        {
            >= OneGibibyte => $"{bytes / (double)OneGibibyte:0.0} GB",
            >= OneMebibyte => $"{bytes / (double)OneMebibyte:0} MB",
            >= OneKibibyte => $"{bytes / (double)OneKibibyte:0} KB",
            _ => $"{bytes} B"
        };
    }
}
