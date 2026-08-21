// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Memory;

namespace EventLogExpert.Runtime.StatusBar;

public readonly record struct LoadingSummary(string Text, int FailedEvents);

public static class StatusBarFormatter
{
    public static string CoverageChipTooltip(int unresolved, int total) =>
        $"{unresolved:N0} unresolved of {total:N0} events loaded in this tab/group. " +
        "Filters are not applied - open Coverage for the current view's breakdown.";

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

    public static string FormatCoverageChip(int unresolved) => $"{unresolved:N0} unresolved";

    public static LoadingSummary? FormatLoading(IReadOnlyDictionary<StatusActivityId, LoadingProgress> activities)
    {
        var loadingCount = activities.Count;

        if (loadingCount == 0) { return null; }

        var totalFailed = 0;
        var singleLoaded = 0;

        foreach (var progress in activities.Values)
        {
            totalFailed += progress.Failed;
            singleLoaded = progress.Loaded;
        }

        var text = loadingCount switch
        {
            1 when singleLoaded == 0 => "Loading...",
            1 => $"Loading: {singleLoaded:N0}",
            _ => $"Loading {loadingCount:N0} logs..."
        };

        return new LoadingSummary(text, totalFailed);
    }

    public static string FormatMemory(long usedMebibytes, MemoryUsageLevel level)
    {
        var value = FormatMebibytes(usedMebibytes);

        // The level word is rendered in the chip so the band is never conveyed by color alone (WCAG 1.4.1).
        return level switch
        {
            MemoryUsageLevel.High => $"Memory: {value} \u00b7 High",
            MemoryUsageLevel.Elevated => $"Memory: {value} \u00b7 Elevated",
            _ => $"Memory: {value}"
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

    /// <summary>
    ///     The screen-reader text for the memory band. Announced only when the effective level transitions (the chip
    ///     value itself is not announced); the initial Normal content is not announced on mount.
    /// </summary>
    public static string MemoryLevelAnnouncement(MemoryUsageLevel level) => level switch
    {
        MemoryUsageLevel.High => "Memory usage high",
        MemoryUsageLevel.Elevated => "Memory usage elevated",
        _ => "Memory usage normal"
    };

    /// <summary>The CSS modifier class for the memory chip's color band; empty for <see cref="MemoryUsageLevel.Normal" />.</summary>
    public static string MemoryLevelClass(MemoryUsageLevel level) => level switch
    {
        MemoryUsageLevel.High => "status-bar-memory-high",
        MemoryUsageLevel.Elevated => "status-bar-memory-elevated",
        _ => string.Empty
    };

    /// <summary>
    ///     The indicator tooltip. Explains that the visible number is the managed heap (which drops as logs close), while
    ///     the process working set - closer to Task Manager's figure - may be released later by the OS.
    /// </summary>
    public static string MemoryTooltip(long usedMebibytes, long workingSetBytes, MemoryUsageLevel level)
    {
        var levelSuffix = level switch
        {
            MemoryUsageLevel.High => " Level: high.",
            MemoryUsageLevel.Elevated => " Level: elevated.",
            _ => string.Empty
        };

        return $"Managed heap (app data): {FormatMebibytes(usedMebibytes)} - drops as logs close. " +
            $"Process working set: {FormatBytes(workingSetBytes)} - the OS may release this later.{levelSuffix}";
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

    private static string FormatMebibytes(long mebibytes) =>
        mebibytes >= 1024 ? $"{mebibytes / 1024.0:0.0} GB" : $"{mebibytes:N0} MB";
}
