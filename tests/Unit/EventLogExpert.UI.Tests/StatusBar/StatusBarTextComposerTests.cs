// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Localization;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Memory;
using EventLogExpert.Runtime.StatusBar;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.Localization;
using System.Collections.Immutable;
using System.Globalization;

namespace EventLogExpert.UI.Tests.StatusBar;

public sealed class StatusBarTextComposerTests : IDisposable
{
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

    public StatusBarTextComposerTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        _localizer = new MarkerLocalizer();
    }

    private IStringLocalizer<SharedResource> Localizer => _localizer;

    [Fact]
    public void CoverageChipTooltip_LoadedCountAndUnresolvedCount_RoutesKeyWithBothCounts() =>
        Assert.Equal(
            "[[StatusBar_Coverage_Tooltip(1,500|2,500)]]",
            StatusBarTextComposer.CoverageTooltip(Localizer, 1500, 2500));

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUiCulture;
    }

    [Theory]
    [InlineData(1, "[[StatusBar_Filter_Lens_One]]")]
    [InlineData(3, "[[StatusBar_Filter_Lens_Many(3)]]")]
    public void FilterIndicatorTooltip_LensesOnly_ReturnsLensCount(int lensCount, string expected) =>
        Assert.Equal(expected, StatusBarTextComposer.FilterIndicatorTooltip(Localizer, persistentActive: false, lensCount: lensCount));

    [Fact]
    public void FilterIndicatorTooltip_NoNarrowing_ReturnsNull() =>
        Assert.Null(StatusBarTextComposer.FilterIndicatorTooltip(Localizer, persistentActive: false, lensCount: 0));

    [Fact]
    public void FilterIndicatorTooltip_PersistentAndLenses_CombinesBoth() =>
        Assert.Equal("[[StatusBar_Filter_ActiveLens_Many(2)]]", StatusBarTextComposer.FilterIndicatorTooltip(Localizer, persistentActive: true, lensCount: 2));

    [Fact]
    public void FilterIndicatorTooltip_PersistentOnly_ReturnsFilterActive() =>
        Assert.Equal("[[StatusBar_Filter_Active]]", StatusBarTextComposer.FilterIndicatorTooltip(Localizer, persistentActive: true, lensCount: 0));

    [Theory]
    [InlineData(DisplayIndicatorKind.Fault, true, true, true, "Error: No resolver", "Error: No resolver")]
    [InlineData(DisplayIndicatorKind.Fault, true, true, true, "", "[[StatusBar_Activity_Fault]]")]
    [InlineData(DisplayIndicatorKind.EmptyPending, true, false, false, "", "[[StatusBar_Activity_Loading]]")]
    [InlineData(DisplayIndicatorKind.EmptyPending, false, false, false, "", "[[StatusBar_Activity_LoadingEvents]]")]
    [InlineData(DisplayIndicatorKind.ReorderPending, false, false, false, "", "[[StatusBar_Activity_Reordering]]")]
    [InlineData(DisplayIndicatorKind.ReorderPending, false, false, true, "", "[[StatusBar_Activity_Reordering]]")]
    [InlineData(DisplayIndicatorKind.None, false, false, true, "", "[[StatusBar_Activity_ContinuouslyUpdating]]")]
    [InlineData(DisplayIndicatorKind.None, false, false, false, "", "")]
    public void FormatActivityAnnouncement_RanksTheDisplaysOwnNewsAgainstTheLogs(
        DisplayIndicatorKind indicator,
        bool isLoading,
        bool bufferFull,
        bool continuouslyUpdating,
        string resolverStatus,
        string expected)
    {
        string actual = StatusBarTextComposer.ActivityAnnouncement(Localizer,
            isLoading, bufferFull, continuouslyUpdating, resolverStatus, indicator);

        Assert.Equal(expected, actual);
        if (!string.IsNullOrEmpty(resolverStatus))
        {
            Assert.DoesNotContain("[[", actual, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(true, false, false, "Error: Failed to load System", "Error: Failed to load System")]
    [InlineData(true, false, false, "", "[[StatusBar_Activity_Loading]]")]
    [InlineData(false, true, false, "", "[[StatusBar_Activity_BufferFull]]")]
    [InlineData(false, false, true, "", "[[StatusBar_Activity_ContinuouslyUpdating]]")]
    [InlineData(false, false, false, "Error: No resolver", "Error: No resolver")]
    [InlineData(false, false, false, "", "")]
    public void FormatActivityAnnouncement_SurfacesErrorOverLoading(
        bool isLoading, bool bufferFull, bool continuouslyUpdating, string resolverStatus, string expected)
    {
        string actual = StatusBarTextComposer.ActivityAnnouncement(Localizer, isLoading, bufferFull, continuouslyUpdating, resolverStatus);

        Assert.Equal(expected, actual);
        if (!string.IsNullOrEmpty(resolverStatus))
        {
            Assert.DoesNotContain("[[", actual, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FormatActivityAnnouncement_WithoutADisplayIndicator_IsUnchanged() =>
        Assert.Equal(
            "[[StatusBar_Activity_Loading]]",
            StatusBarTextComposer.ActivityAnnouncement(Localizer,
                isLoading: true, bufferFull: false, continuouslyUpdating: false, resolverStatus: ""));

    [Fact]
    public void FormatCounts_Filtered_ShowsShownOfTotal() =>
        Assert.Equal(
            "[[StatusBar_Counts_ShownOfTotal(200|1,234)]]",
            StatusBarTextComposer.Counts(Localizer, 1234, 200, isFiltered: true, selectedCount: 0));

    [Fact]
    public void FormatCounts_MultiSelect_AppendsSelectedSuffix() =>
        Assert.Equal(
            "[[StatusBar_Counts_ShownOfTotalSelected(200|1,234|3)]]",
            StatusBarTextComposer.Counts(Localizer, 1234, 200, isFiltered: true, selectedCount: 3));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void FormatCounts_SingleOrNoSelection_OmitsSelectedSuffix(int selectedCount) =>
        Assert.Equal(
            "[[StatusBar_Counts_Total_Many(1,234)]]",
            StatusBarTextComposer.Counts(Localizer, 1234, 1234, isFiltered: false, selectedCount: selectedCount));

    [Fact]
    public void FormatCounts_Unfiltered_ShowsTotalEvents() =>
        Assert.Equal("[[StatusBar_Counts_Total_Many(1,234)]]", StatusBarTextComposer.Counts(Localizer, 1234, 1234, isFiltered: false, selectedCount: 0));

    [Fact]
    public void FormatCoverageChip_UnresolvedCount_RoutesKeyWithGroupedCount() =>
        Assert.Equal("[[StatusBar_Coverage_Chip(1,500)]]", StatusBarTextComposer.CoverageChip(Localizer, 1500));

    [Fact]
    public void FormatLoading_FailureOnlyProgressRoundingToZero_StaysPending()
    {
        var summary = StatusBarTextComposer.Loading(Localizer, Loading(new LoadingProgress(0, 3, 10_000)));

        Assert.NotNull(summary);
        Assert.Equal("[[StatusBar_Loading_Pending]]", summary.Value.Text);
    }

    [Fact]
    public void FormatLoading_FailureOnlyProgressWithTotal_ShowsPercentWithoutZeroCount()
    {
        var summary = StatusBarTextComposer.Loading(Localizer, Loading(new LoadingProgress(0, 50_000, 100_000)));

        Assert.NotNull(summary);
        Assert.Equal("[[StatusBar_Loading_PendingPercent(50)]]", summary.Value.Text);
        Assert.DoesNotContain("[[StatusBar_Loading_CountPercent(0|50)]]", summary.Value.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatLoading_ManyActivities_GroupsLogCountAndSumsFailedEvents()
    {
        var progresses = Enumerable.Range(0, 1500)
            .Select(_ => new LoadingProgress(10, 1))
            .ToArray();

        var summary = StatusBarTextComposer.Loading(Localizer, Loading(progresses));

        Assert.NotNull(summary);
        Assert.Equal("[[StatusBar_Loading_ManyLogs(1,500)]]", summary.Value.Text);
        Assert.Equal(1500, summary.Value.FailedEvents);
    }

    [Fact]
    public void FormatLoading_NoActivities_ReturnsNull() =>
        Assert.Null(StatusBarTextComposer.Loading(Localizer, Loading()));

    [Fact]
    public void FormatLoading_NonPositiveTotal_FallsBackToCountOnly()
    {
        var summary = StatusBarTextComposer.Loading(Localizer, Loading(new LoadingProgress(4_500, 0, 0)));

        Assert.NotNull(summary);
        Assert.Equal("[[StatusBar_Loading_Count(4,500)]]", summary.Value.Text);
    }

    [Fact]
    public void FormatLoading_PercentClampsAtNinetyNineBeforeCompletion()
    {
        var summary = StatusBarTextComposer.Loading(Localizer, Loading(new LoadingProgress(1_000_000, 0, 1_000_000)));

        Assert.NotNull(summary);
        Assert.Equal("[[StatusBar_Loading_CountPercent(1,000,000|99)]]", summary.Value.Text);
    }

    [Fact]
    public void FormatLoading_PercentUsesLoadedPlusFailed()
    {
        var summary = StatusBarTextComposer.Loading(Localizer, Loading(new LoadingProgress(4_000, 1_000, 10_000)));

        Assert.NotNull(summary);
        Assert.Equal("[[StatusBar_Loading_CountPercent(4,000|50)]]", summary.Value.Text);
    }

    [Fact]
    public void FormatLoading_SingleActivityAtZero_ShowsPendingText()
    {
        var summary = StatusBarTextComposer.Loading(Localizer, Loading(new LoadingProgress(0, 0)));

        Assert.NotNull(summary);
        Assert.Equal("[[StatusBar_Loading_Pending]]", summary.Value.Text);
        Assert.Equal(0, summary.Value.FailedEvents);
    }

    [Fact]
    public void FormatLoading_SingleActivityWithProgress_GroupsTheLoadedCount()
    {
        var summary = StatusBarTextComposer.Loading(Localizer, Loading(new LoadingProgress(12_345, 0)));

        Assert.NotNull(summary);
        Assert.Equal("[[StatusBar_Loading_Count(12,345)]]", summary.Value.Text);
    }

    [Fact]
    public void FormatLoading_SingleActivityWithTotal_AppendsPercent()
    {
        var summary = StatusBarTextComposer.Loading(Localizer, Loading(new LoadingProgress(4_500, 0, 10_000)));

        Assert.NotNull(summary);
        Assert.Equal("[[StatusBar_Loading_CountPercent(4,500|45)]]", summary.Value.Text);
    }

    [Fact]
    public void FormatLoading_SingleFailedOnlyActivity_StaysPendingAndReportsFailures()
    {
        var summary = StatusBarTextComposer.Loading(Localizer, Loading(new LoadingProgress(0, 3)));

        Assert.NotNull(summary);
        Assert.Equal("[[StatusBar_Loading_Pending]]", summary.Value.Text);
        Assert.Equal(3, summary.Value.FailedEvents);
    }

    [Fact]
    public void FormatLoading_TinyCorruptDenominator_ClampsInsteadOfOverflowing()
    {
        var summary = StatusBarTextComposer.Loading(Localizer, Loading(new LoadingProgress(50_000_000, 0, 2)));

        Assert.NotNull(summary);
        Assert.Equal("[[StatusBar_Loading_CountPercent(50,000,000|99)]]", summary.Value.Text);
    }

    [Fact]
    public void FormatLoading_TwoActivities_SwitchesToLogCount()
    {
        var summary = StatusBarTextComposer.Loading(Localizer,
            Loading(new LoadingProgress(100, 0), new LoadingProgress(50, 0)));

        Assert.NotNull(summary);
        Assert.Equal("[[StatusBar_Loading_ManyLogs(2)]]", summary.Value.Text);
    }

    [Fact]
    public void FormatMemory_Elevated_AppendsTheLevelWord() =>
        Assert.Equal("[[StatusBar_Memory_Value_Elevated(1.0 GB)]]", StatusBarTextComposer.MemoryValue(Localizer, 1024, MemoryUsageLevel.Elevated));

    [Fact]
    public void FormatMemory_High_AppendsTheLevelWord() =>
        Assert.Equal("[[StatusBar_Memory_Value_High(2.0 GB)]]", StatusBarTextComposer.MemoryValue(Localizer, 2048, MemoryUsageLevel.High));

    [Theory]
    [InlineData(0, "[[StatusBar_Memory_Value_Normal(0 MB)]]")]
    [InlineData(512, "[[StatusBar_Memory_Value_Normal(512 MB)]]")]
    [InlineData(1536, "[[StatusBar_Memory_Value_Normal(1.5 GB)]]")]
    public void FormatMemory_Normal_ShowsValueWithoutTheLevelWord(long usedMebibytes, string expected) =>
        Assert.Equal(expected, StatusBarTextComposer.MemoryValue(Localizer, usedMebibytes, MemoryUsageLevel.Normal));

    [Fact]
    public void FormatSource_AllLogs_CountsOnlyStandaloneTabs()
    {
        var allLogs = Combined(LogTabGroupId.AllLogs);
        var eventTables = new[] { Channel("Application"), Channel("System"), allLogs };

        Assert.Equal("[[StatusBar_Source_AllLogs(2)]]", StatusBarTextComposer.Source(Localizer, allLogs, eventTables, []));
    }

    [Fact]
    public void FormatSource_Channel_ReturnsLogName() =>
        AssertVerbatim("Application", StatusBarTextComposer.Source(Localizer, Channel("Application"), [], []));

    [Fact]
    public void FormatSource_NamedGroup_ReturnsGroupName()
    {
        var groupId = LogTabGroupId.Create();
        var active = Combined(groupId);
        var groups = new[] { new LogTabGroup(groupId, "Incident triage", [EventLogId.Create(), EventLogId.Create()]) };

        AssertVerbatim("Incident triage", StatusBarTextComposer.Source(Localizer, active, [active], groups));
    }

    [Fact]
    public void FormatSource_NoActiveView_ReturnsNoLogOpen() =>
        Assert.Equal("[[StatusBar_Source_None]]", StatusBarTextComposer.Source(Localizer, null, [], []));

    [Fact]
    public void FormatSource_OpenedFile_ReturnsBaseName() =>
        AssertVerbatim("Security.evtx", StatusBarTextComposer.Source(Localizer, File(@"C:\logs\Security.evtx"), [], []));

    [Fact]
    public void FormatSource_UnnamedGroup_ReturnsCombinedWithMemberCount()
    {
        var groupId = LogTabGroupId.Create();
        var active = Combined(groupId);
        var groups = new[] { new LogTabGroup(groupId, string.Empty, [EventLogId.Create(), EventLogId.Create(), EventLogId.Create()]) };

        Assert.Equal("[[StatusBar_Source_CombinedCount_Many(3)]]", StatusBarTextComposer.Source(Localizer, active, [active], groups));
    }

    [Theory]
    [InlineData(MemoryUsageLevel.Normal, "[[StatusBar_Memory_Announce_Normal]]")]
    [InlineData(MemoryUsageLevel.Elevated, "[[StatusBar_Memory_Announce_Elevated]]")]
    [InlineData(MemoryUsageLevel.High, "[[StatusBar_Memory_Announce_High]]")]
    public void MemoryLevelAnnouncement_MapsBandToSpokenText(MemoryUsageLevel level, string expected) =>
        Assert.Equal(expected, MemoryUsageLevelLocalizer.Announcement(Localizer, level));

    [Theory]
    [InlineData(MemoryUsageLevel.Normal, "")]
    [InlineData(MemoryUsageLevel.Elevated, "status-bar-memory-elevated")]
    [InlineData(MemoryUsageLevel.High, "status-bar-memory-high")]
    public void MemoryLevelClass_MapsBandToModifier(MemoryUsageLevel level, string expected) =>
        Assert.Equal(expected, StatusBarTextComposer.MemoryLevelClass(level));

    [Fact]
    public void MemoryTooltip_DistinguishesManagedHeapFromWorkingSet()
    {
        var tooltip = StatusBarTextComposer.MemoryTooltip(Localizer, 512, 900L * 1024 * 1024, MemoryUsageLevel.High);

        Assert.Equal("[[StatusBar_Memory_Tooltip_High(512 MB|900 MB)]]", tooltip);
    }

    [Fact]
    public void MemoryTooltip_Elevated_RoutesElevatedKeyWithBothSizes() =>
        Assert.Equal(
            "[[StatusBar_Memory_Tooltip_Elevated(256 MB|512 MB)]]",
            StatusBarTextComposer.MemoryTooltip(Localizer, 256, 512L * 1024 * 1024, MemoryUsageLevel.Elevated));

    [Fact]
    public void MemoryTooltip_Normal_OmitsTheLevelSuffix() =>
        Assert.Equal(
            "[[StatusBar_Memory_Tooltip_Normal(100 MB|200 B)]]",
            StatusBarTextComposer.MemoryTooltip(Localizer, 100, 200, MemoryUsageLevel.Normal));

    private static void AssertVerbatim(string expected, string actual)
    {
        Assert.Equal(expected, actual);
        Assert.DoesNotContain("[[", actual, StringComparison.Ordinal);
    }

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
