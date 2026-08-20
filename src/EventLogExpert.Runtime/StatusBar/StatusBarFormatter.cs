// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.LogTable;

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
}
