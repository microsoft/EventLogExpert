// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Memory;
using EventLogExpert.Runtime.StatusBar;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.StatusBar;

public sealed class StatusBarFormatterTests
{
    [Theory]
    [InlineData(1, "1 lens")]
    [InlineData(3, "3 lenses")]
    public void FilterIndicatorTooltip_LensesOnly_ReturnsLensCount(int lensCount, string expected) =>
        Assert.Equal(expected, StatusBarFormatter.FilterIndicatorTooltip(persistentActive: false, lensCount));

    [Fact]
    public void FilterIndicatorTooltip_NoNarrowing_ReturnsNull() =>
        Assert.Null(StatusBarFormatter.FilterIndicatorTooltip(persistentActive: false, lensCount: 0));

    [Fact]
    public void FilterIndicatorTooltip_PersistentAndLenses_CombinesBoth() =>
        Assert.Equal("Filter + 2 lenses", StatusBarFormatter.FilterIndicatorTooltip(persistentActive: true, lensCount: 2));

    [Fact]
    public void FilterIndicatorTooltip_PersistentOnly_ReturnsFilterActive() =>
        Assert.Equal("Filter active", StatusBarFormatter.FilterIndicatorTooltip(persistentActive: true, lensCount: 0));

    [Theory]
    [InlineData(DisplayIndicatorKind.Fault, true, true, true, "Error: No resolver", "Error: No resolver")]
    [InlineData(DisplayIndicatorKind.Fault, true, true, true, "", "These events could not be prepared")]
    [InlineData(DisplayIndicatorKind.EmptyPending, true, false, false, "", "Loading")]
    [InlineData(DisplayIndicatorKind.EmptyPending, false, false, false, "", "Loading events")]
    [InlineData(DisplayIndicatorKind.ReorderPending, false, false, false, "", "Reordering events")]
    [InlineData(DisplayIndicatorKind.ReorderPending, false, false, true, "", "Reordering events")]
    [InlineData(DisplayIndicatorKind.None, false, false, true, "", "Continuously updating")]
    [InlineData(DisplayIndicatorKind.None, false, false, false, "", "")]
    public void FormatActivityAnnouncement_RanksTheDisplaysOwnNewsAgainstTheLogs(
        DisplayIndicatorKind indicator,
        bool isLoading,
        bool bufferFull,
        bool continuouslyUpdating,
        string resolverStatus,
        string expected) =>
        Assert.Equal(
            expected,
            StatusBarFormatter.FormatActivityAnnouncement(
                isLoading, bufferFull, continuouslyUpdating, resolverStatus, indicator));

    [Theory]
    [InlineData(true, false, false, "Error: Failed to load System", "Error: Failed to load System")]
    [InlineData(true, false, false, "", "Loading")]
    [InlineData(false, true, false, "", "Buffer full")]
    [InlineData(false, false, true, "", "Continuously updating")]
    [InlineData(false, false, false, "Error: No resolver", "Error: No resolver")]
    [InlineData(false, false, false, "", "")]
    public void FormatActivityAnnouncement_SurfacesErrorOverLoading(
        bool isLoading, bool bufferFull, bool continuouslyUpdating, string resolverStatus, string expected) =>
        Assert.Equal(
            expected,
            StatusBarFormatter.FormatActivityAnnouncement(isLoading, bufferFull, continuouslyUpdating, resolverStatus));

    [Fact]
    public void FormatActivityAnnouncement_WithoutADisplayIndicator_IsUnchanged() =>
        Assert.Equal(
            "Loading",
            StatusBarFormatter.FormatActivityAnnouncement(
                isLoading: true, bufferFull: false, continuouslyUpdating: false, resolverStatus: ""));

    [Fact]
    public void FormatCounts_Filtered_ShowsShownOfTotal() =>
        Assert.Equal(
            $"{200:N0} of {1234:N0} shown",
            StatusBarFormatter.FormatCounts(1234, 200, isFiltered: true, selectedCount: 0));

    [Fact]
    public void FormatCounts_MultiSelect_AppendsSelectedSuffix() =>
        Assert.Equal(
            $"{200:N0} of {1234:N0} shown \u00b7 {3:N0} selected",
            StatusBarFormatter.FormatCounts(1234, 200, isFiltered: true, selectedCount: 3));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void FormatCounts_SingleOrNoSelection_OmitsSelectedSuffix(int selectedCount) =>
        Assert.Equal(
            $"{1234:N0} events",
            StatusBarFormatter.FormatCounts(1234, 1234, isFiltered: false, selectedCount));

    [Fact]
    public void FormatCounts_Unfiltered_ShowsTotalEvents() =>
        Assert.Equal($"{1234:N0} events", StatusBarFormatter.FormatCounts(1234, 1234, isFiltered: false, selectedCount: 0));

    [Fact]
    public void FormatLoading_FailureOnlyProgressRoundingToZero_StaysPending()
    {
        var summary = StatusBarFormatter.FormatLoading(Loading(new LoadingProgress(0, 3, 10_000)));

        Assert.NotNull(summary);
        Assert.Equal("Loading...", summary.Value.Text);
    }

    [Fact]
    public void FormatLoading_FailureOnlyProgressWithTotal_ShowsPercentWithoutZeroCount()
    {
        var summary = StatusBarFormatter.FormatLoading(Loading(new LoadingProgress(0, 50_000, 100_000)));

        Assert.NotNull(summary);
        Assert.Equal("Loading... (50%)", summary.Value.Text);
        Assert.DoesNotContain("Loading: 0", summary.Value.Text);
    }

    [Fact]
    public void FormatLoading_ManyActivities_GroupsLogCountAndSumsFailedEvents()
    {
        var progresses = Enumerable.Range(0, 1500)
            .Select(_ => new LoadingProgress(10, 1))
            .ToArray();

        var summary = StatusBarFormatter.FormatLoading(Loading(progresses));

        Assert.NotNull(summary);
        Assert.Equal($"Loading {1500:N0} logs...", summary.Value.Text);
        Assert.Equal(1500, summary.Value.FailedEvents);
    }

    [Fact]
    public void FormatLoading_NoActivities_ReturnsNull() =>
        Assert.Null(StatusBarFormatter.FormatLoading(Loading()));

    [Fact]
    public void FormatLoading_NonPositiveTotal_FallsBackToCountOnly()
    {
        var summary = StatusBarFormatter.FormatLoading(Loading(new LoadingProgress(4_500, 0, 0)));

        Assert.NotNull(summary);
        Assert.Equal($"Loading: {4500:N0}", summary.Value.Text);
    }

    [Fact]
    public void FormatLoading_PercentClampsAtNinetyNineBeforeCompletion()
    {
        var summary = StatusBarFormatter.FormatLoading(Loading(new LoadingProgress(1_000_000, 0, 1_000_000)));

        Assert.NotNull(summary);
        Assert.Equal($"Loading: {1000000:N0} (99%)", summary.Value.Text);
    }

    [Fact]
    public void FormatLoading_PercentUsesLoadedPlusFailed()
    {
        // 4000 loaded + 1000 failed = 5000 processed of 10000 = 50%; the displayed count stays the loaded tally.
        var summary = StatusBarFormatter.FormatLoading(Loading(new LoadingProgress(4_000, 1_000, 10_000)));

        Assert.NotNull(summary);
        Assert.Equal($"Loading: {4000:N0} (50%)", summary.Value.Text);
    }

    [Fact]
    public void FormatLoading_SingleActivityAtZero_ShowsPendingText()
    {
        var summary = StatusBarFormatter.FormatLoading(Loading(new LoadingProgress(0, 0)));

        Assert.NotNull(summary);
        Assert.Equal("Loading...", summary.Value.Text);
        Assert.Equal(0, summary.Value.FailedEvents);
    }

    [Fact]
    public void FormatLoading_SingleActivityWithProgress_GroupsTheLoadedCount()
    {
        var summary = StatusBarFormatter.FormatLoading(Loading(new LoadingProgress(12_345, 0)));

        Assert.NotNull(summary);
        Assert.Equal($"Loading: {12345:N0}", summary.Value.Text);
    }

    [Fact]
    public void FormatLoading_SingleActivityWithTotal_AppendsPercent()
    {
        var summary = StatusBarFormatter.FormatLoading(Loading(new LoadingProgress(4_500, 0, 10_000)));

        Assert.NotNull(summary);
        Assert.Equal($"Loading: {4500:N0} (45%)", summary.Value.Text);
    }

    [Fact]
    public void FormatLoading_SingleFailedOnlyActivity_StaysPendingAndReportsFailures()
    {
        var summary = StatusBarFormatter.FormatLoading(Loading(new LoadingProgress(0, 3)));

        Assert.NotNull(summary);
        Assert.Equal("Loading...", summary.Value.Text);
        Assert.Equal(3, summary.Value.FailedEvents);
    }

    [Fact]
    public void FormatLoading_TinyCorruptDenominator_ClampsInsteadOfOverflowing()
    {
        // (long)50_000_000 * 100 / 2 = 2.5e9 (> int.MaxValue): clamping in long space yields 99, never a wrapped value.
        var summary = StatusBarFormatter.FormatLoading(Loading(new LoadingProgress(50_000_000, 0, 2)));

        Assert.NotNull(summary);
        Assert.Equal($"Loading: {50000000:N0} (99%)", summary.Value.Text);
    }

    [Fact]
    public void FormatLoading_TwoActivities_SwitchesToLogCount()
    {
        var summary = StatusBarFormatter.FormatLoading(
            Loading(new LoadingProgress(100, 0), new LoadingProgress(50, 0)));

        Assert.NotNull(summary);
        Assert.Equal("Loading 2 logs...", summary.Value.Text);
    }

    [Fact]
    public void FormatMemory_Elevated_AppendsTheLevelWord() =>
        Assert.Equal("Memory: 1.0 GB \u00b7 Elevated", StatusBarFormatter.FormatMemory(1024, MemoryUsageLevel.Elevated));

    [Fact]
    public void FormatMemory_High_AppendsTheLevelWord() =>
        Assert.Equal("Memory: 2.0 GB \u00b7 High", StatusBarFormatter.FormatMemory(2048, MemoryUsageLevel.High));

    [Theory]
    [InlineData(0, "Memory: 0 MB")]
    [InlineData(512, "Memory: 512 MB")]
    [InlineData(1536, "Memory: 1.5 GB")]
    public void FormatMemory_Normal_ShowsValueWithoutTheLevelWord(long usedMebibytes, string expected) =>
        Assert.Equal(expected, StatusBarFormatter.FormatMemory(usedMebibytes, MemoryUsageLevel.Normal));

    [Fact]
    public void FormatSource_AllLogs_CountsOnlyStandaloneTabs()
    {
        var allLogs = Combined(LogTabGroupId.AllLogs);
        var eventTables = new[] { Channel("Application"), Channel("System"), allLogs };

        Assert.Equal("All logs (2)", StatusBarFormatter.FormatSource(allLogs, eventTables, []));
    }

    [Fact]
    public void FormatSource_Channel_ReturnsLogName() =>
        Assert.Equal("Application", StatusBarFormatter.FormatSource(Channel("Application"), [], []));

    [Fact]
    public void FormatSource_NamedGroup_ReturnsGroupName()
    {
        var groupId = LogTabGroupId.Create();
        var active = Combined(groupId);
        var groups = new[] { new LogTabGroup(groupId, "Incident triage", [EventLogId.Create(), EventLogId.Create()]) };

        Assert.Equal("Incident triage", StatusBarFormatter.FormatSource(active, [active], groups));
    }

    [Fact]
    public void FormatSource_NoActiveView_ReturnsNoLogOpen() =>
        Assert.Equal("No log open", StatusBarFormatter.FormatSource(null, [], []));

    [Fact]
    public void FormatSource_OpenedFile_ReturnsBaseName() =>
        Assert.Equal("Security.evtx", StatusBarFormatter.FormatSource(File(@"C:\logs\Security.evtx"), [], []));

    [Fact]
    public void FormatSource_UnnamedGroup_ReturnsCombinedWithMemberCount()
    {
        var groupId = LogTabGroupId.Create();
        var active = Combined(groupId);
        var groups = new[] { new LogTabGroup(groupId, string.Empty, [EventLogId.Create(), EventLogId.Create(), EventLogId.Create()]) };

        Assert.Equal("Combined (3 logs)", StatusBarFormatter.FormatSource(active, [active], groups));
    }

    [Theory]
    [InlineData(MemoryUsageLevel.Normal, "Memory usage normal")]
    [InlineData(MemoryUsageLevel.Elevated, "Memory usage elevated")]
    [InlineData(MemoryUsageLevel.High, "Memory usage high")]
    public void MemoryLevelAnnouncement_MapsBandToSpokenText(MemoryUsageLevel level, string expected) =>
        Assert.Equal(expected, StatusBarFormatter.MemoryLevelAnnouncement(level));

    [Theory]
    [InlineData(MemoryUsageLevel.Normal, "")]
    [InlineData(MemoryUsageLevel.Elevated, "status-bar-memory-elevated")]
    [InlineData(MemoryUsageLevel.High, "status-bar-memory-high")]
    public void MemoryLevelClass_MapsBandToModifier(MemoryUsageLevel level, string expected) =>
        Assert.Equal(expected, StatusBarFormatter.MemoryLevelClass(level));

    [Fact]
    public void MemoryTooltip_DistinguishesManagedHeapFromWorkingSet()
    {
        var tooltip = StatusBarFormatter.MemoryTooltip(512, 900L * 1024 * 1024, MemoryUsageLevel.High);

        Assert.Contains("Managed heap (app data): 512 MB", tooltip);
        Assert.Contains("Process working set: 900 MB", tooltip);
        Assert.Contains("Level: high.", tooltip);
    }

    [Fact]
    public void MemoryTooltip_Normal_OmitsTheLevelSuffix() =>
        Assert.DoesNotContain("Level:", StatusBarFormatter.MemoryTooltip(100, 200, MemoryUsageLevel.Normal));

    private static LogView Channel(string name) =>
        new(EventLogId.Create()) { LogName = name, LogPathType = LogPathType.Channel };

    private static LogView Combined(LogTabGroupId groupId) =>
        new(EventLogId.Create()) { GroupId = groupId };

    private static LogView File(string path) =>
        new(EventLogId.Create()) { FileName = path, LogPathType = LogPathType.File };

    private static ImmutableDictionary<StatusActivityId, LoadingProgress> Loading(params LoadingProgress[] progresses)
    {
        var builder = ImmutableDictionary.CreateBuilder<StatusActivityId, LoadingProgress>();

        foreach (var progress in progresses)
        {
            builder.Add(StatusActivityId.Create(), progress);
        }

        return builder.ToImmutable();
    }
}
