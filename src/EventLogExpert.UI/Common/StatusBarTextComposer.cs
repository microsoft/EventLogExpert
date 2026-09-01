// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Memory;
using EventLogExpert.Runtime.StatusBar;
using Microsoft.Extensions.Localization;
using System.Globalization;

namespace EventLogExpert.UI.Common;

internal readonly record struct StatusBarLoadingChip(string Text, int FailedEvents);

internal static class StatusBarTextComposer
{
    internal static string ActivityAnnouncement(
        IStringLocalizer<SharedResource> localizer,
        bool isLoading,
        bool bufferFull,
        bool continuouslyUpdating,
        ResolverStatus resolver,
        DisplayIndicatorKind displayIndicator = DisplayIndicatorKind.None)
    {
        if (resolver.Reason != ResolverStatusReason.None) { return ResolverStatusLocalizer.Describe(localizer, resolver); }

        if (displayIndicator == DisplayIndicatorKind.Fault) { return localizer["StatusBar_Activity_Fault"]; }

        if (bufferFull) { return localizer["StatusBar_Activity_BufferFull"]; }

        if (isLoading) { return localizer["StatusBar_Activity_Loading"]; }

        return displayIndicator switch
        {
            DisplayIndicatorKind.EmptyPending => localizer["StatusBar_Activity_LoadingEvents"],
            DisplayIndicatorKind.ReorderPending => localizer["StatusBar_Activity_Reordering"],
            DisplayIndicatorKind.None => continuouslyUpdating ? localizer["StatusBar_Activity_ContinuouslyUpdating"] : string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(displayIndicator), displayIndicator, null)
        };
    }

    internal static string Counts(
        IStringLocalizer<SharedResource> localizer,
        int total,
        int shown,
        bool isFiltered,
        int selectedCount)
    {
        string formattedTotal = FormatCount(total);
        string formattedShown = FormatCount(shown);

        return (isFiltered, selectedCount >= 2) switch
        {
            (false, false) => localizer[total == 1 ? "StatusBar_Counts_Total_One" : "StatusBar_Counts_Total_Many", formattedTotal],
            (false, true) => localizer["StatusBar_Counts_TotalSelected", formattedTotal, FormatCount(selectedCount)],
            (true, false) => localizer["StatusBar_Counts_ShownOfTotal", formattedShown, formattedTotal],
            _ => localizer["StatusBar_Counts_ShownOfTotalSelected", formattedShown, formattedTotal, FormatCount(selectedCount)]
        };
    }

    internal static string CoverageAriaLabel(IStringLocalizer<SharedResource> localizer, int unresolved) =>
        localizer["StatusBar_Coverage_AriaLabel", FormatCount(unresolved)];

    internal static string CoverageChip(IStringLocalizer<SharedResource> localizer, int unresolved) =>
        localizer["StatusBar_Coverage_Chip", FormatCount(unresolved)];

    internal static string CoverageTooltip(IStringLocalizer<SharedResource> localizer, int unresolved, int total) =>
        localizer["StatusBar_Coverage_Tooltip", FormatCount(unresolved), FormatCount(total)];

    internal static string? FilterIndicatorTooltip(IStringLocalizer<SharedResource> localizer, bool persistentActive, int lensCount)
    {
        if (lensCount <= 0)
        {
            return persistentActive ? localizer["StatusBar_Filter_Active"].Value : null;
        }

        if (persistentActive)
        {
            return lensCount == 1 ?
                localizer["StatusBar_Filter_ActiveLens_One"].Value :
                localizer["StatusBar_Filter_ActiveLens_Many", FormatRaw(lensCount)].Value;
        }

        return lensCount == 1 ?
            localizer["StatusBar_Filter_Lens_One"].Value :
            localizer["StatusBar_Filter_Lens_Many", FormatRaw(lensCount)].Value;
    }

    internal static StatusBarLoadingChip? Loading(
        IStringLocalizer<SharedResource> localizer,
        IReadOnlyDictionary<StatusActivityId, LoadingProgress> activities)
    {
        var loadingCount = activities.Count;

        if (loadingCount == 0) { return null; }

        var totalFailed = 0;
        var singleLoaded = 0;
        var singleFailed = 0;
        long? singleTotal = null;

        foreach (LoadingProgress progress in activities.Values)
        {
            totalFailed += progress.Failed;
            singleLoaded = progress.Loaded;
            singleFailed = progress.Failed;
            singleTotal = progress.Total;
        }

        int? percent = Percent(singleLoaded, singleFailed, singleTotal);

        string text = loadingCount switch
        {
            1 when singleLoaded == 0 && percent is int failureOnlyPercent and > 0 => localizer["StatusBar_Loading_PendingPercent", FormatRaw(failureOnlyPercent)],
            1 when singleLoaded == 0 => localizer["StatusBar_Loading_Pending"],
            1 when percent is { } loadedPercent => localizer["StatusBar_Loading_CountPercent", FormatCount(singleLoaded), FormatRaw(loadedPercent)],
            1 => localizer["StatusBar_Loading_Count", FormatCount(singleLoaded)],
            _ => localizer["StatusBar_Loading_ManyLogs", FormatCount(loadingCount)]
        };

        return new StatusBarLoadingChip(text, totalFailed);
    }

    internal static string LoadingFailedText(IStringLocalizer<SharedResource> localizer, int failedEvents) =>
        localizer["StatusBar_Loading_Failed", FormatCount(failedEvents)];

    internal static string MemoryLevelClass(MemoryUsageLevel level) => level switch
    {
        MemoryUsageLevel.Normal => string.Empty,
        MemoryUsageLevel.Elevated => "status-bar-memory-elevated",
        MemoryUsageLevel.High => "status-bar-memory-high",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
    };

    internal static string MemoryTooltip(
        IStringLocalizer<SharedResource> localizer,
        long usedMebibytes,
        long workingSetBytes,
        MemoryUsageLevel level)
    {
        string used = FormatMebibytes(usedMebibytes);
        string workingSet = FormatBytes(workingSetBytes);

        return level switch
        {
            MemoryUsageLevel.Normal => localizer["StatusBar_Memory_Tooltip_Normal", used, workingSet],
            MemoryUsageLevel.Elevated => localizer["StatusBar_Memory_Tooltip_Elevated", used, workingSet],
            MemoryUsageLevel.High => localizer["StatusBar_Memory_Tooltip_High", used, workingSet],
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
        };
    }

    internal static string MemoryValue(IStringLocalizer<SharedResource> localizer, long usedMebibytes, MemoryUsageLevel level)
    {
        string value = FormatMebibytes(usedMebibytes);

        return level switch
        {
            MemoryUsageLevel.Normal => localizer["StatusBar_Memory_Value_Normal", value],
            MemoryUsageLevel.Elevated => localizer["StatusBar_Memory_Value_Elevated", value],
            MemoryUsageLevel.High => localizer["StatusBar_Memory_Value_High", value],
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
        };
    }

    internal static string NewEventsLabel(IStringLocalizer<SharedResource> localizer, int newEventCount) =>
        localizer["StatusBar_NewEvents_Label", FormatRaw(newEventCount)];

    internal static string Source(
        IStringLocalizer<SharedResource> localizer,
        LogView? active,
        IReadOnlyList<LogView> eventTables,
        IReadOnlyList<LogTabGroup> groups)
    {
        if (active is null) { return localizer["StatusBar_Source_None"]; }

        if (active.GroupId is not { } groupId)
        {
            return active.FileName is { } fileName ? Path.GetFileName(fileName) : active.LogName;
        }

        if (groupId.IsAll)
        {
            var openLogs = 0;

            foreach (LogView table in eventTables)
            {
                if (!table.IsCombined) { openLogs++; }
            }

            return localizer["StatusBar_Source_AllLogs", FormatRaw(openLogs)];
        }

        LogTabGroup? group = groups.FirstOrDefault(candidate => candidate.Id == groupId);

        if (group is null) { return localizer["StatusBar_Source_Combined"]; }

        int memberCount = group.MemberIds.Count;

        return string.IsNullOrEmpty(group.Name) ?
            localizer[memberCount == 1 ? "StatusBar_Source_CombinedCount_One" : "StatusBar_Source_CombinedCount_Many", FormatRaw(memberCount)] :
            group.Name;
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

    private static string FormatCount(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private static string FormatMebibytes(long mebibytes) =>
        mebibytes >= 1024 ? $"{mebibytes / 1024.0:0.0} GB" : $"{mebibytes:N0} MB";

    private static string FormatRaw(int value) => value.ToString(CultureInfo.CurrentCulture);

    private static int? Percent(int loaded, int failed, long? total) =>
        total is { } denominator and > 0 ?
            (int)Math.Clamp(((long)loaded + failed) * 100 / denominator, 0L, 99L) :
            null;
}
