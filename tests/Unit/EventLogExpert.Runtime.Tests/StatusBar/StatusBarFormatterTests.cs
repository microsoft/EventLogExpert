// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.StatusBar;

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

    private static LogView Channel(string name) =>
        new(EventLogId.Create()) { LogName = name, LogPathType = LogPathType.Channel };

    private static LogView Combined(LogTabGroupId groupId) =>
        new(EventLogId.Create()) { GroupId = groupId };

    private static LogView File(string path) =>
        new(EventLogId.Create()) { FileName = path, LogPathType = LogPathType.File };
}
