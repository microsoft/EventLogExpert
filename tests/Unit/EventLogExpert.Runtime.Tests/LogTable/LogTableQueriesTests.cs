// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Runtime.LogTable;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class LogTableQueriesTests
{
    [Fact]
    public void GetTabGroups_ReturnsTheCurrentGroups()
    {
        var groupId = LogTabGroupId.Create();
        var state = new LogTableState
        {
            Groups = [new LogTabGroup(groupId, "Group", ImmutableHashSet<EventLogId>.Empty)]
        };

        var groups = new LogTableQueries(StateReturning(state)).GetTabGroups();

        Assert.Single(groups);
        Assert.Equal(groupId, groups[0].Id);
    }

    [Fact]
    public void HasActiveLogs_WhenANonCombinedTab_IsTrue_EvenWhileLoading()
    {
        var state = new LogTableState
        {
            EventTables = [new LogView(EventLogId.Create()) { LogName = "Application", IsLoading = true }]
        };

        Assert.True(new LogTableQueries(StateReturning(state)).HasActiveLogs());
    }

    [Fact]
    public void HasActiveLogs_WhenNoTabs_IsFalse()
    {
        var queries = new LogTableQueries(StateReturning(new LogTableState()));

        Assert.False(queries.HasActiveLogs());
    }

    [Fact]
    public void HasActiveLogs_WhenOnlyCombinedTab_IsFalse()
    {
        var state = new LogTableState
        {
            EventTables = [new LogView(EventLogId.Create()) { GroupId = LogTabGroupId.AllLogs }]
        };

        Assert.False(new LogTableQueries(StateReturning(state)).HasActiveLogs());
    }

    [Fact]
    public void HasMultipleIndividualTabs_ExcludesCombinedTabs()
    {
        var state = new LogTableState
        {
            EventTables =
            [
                new LogView(EventLogId.Create()) { GroupId = LogTabGroupId.AllLogs },
                new LogView(EventLogId.Create()) { LogName = "Alpha" }
            ]
        };

        Assert.False(new LogTableQueries(StateReturning(state)).HasMultipleIndividualTabs());
    }

    [Fact]
    public void HasMultipleIndividualTabs_WhenTwoIndividualTabs_IsTrue()
    {
        var state = new LogTableState
        {
            EventTables =
            [
                new LogView(EventLogId.Create()) { LogName = "Alpha" },
                new LogView(EventLogId.Create()) { LogName = "Beta" }
            ]
        };

        Assert.True(new LogTableQueries(StateReturning(state)).HasMultipleIndividualTabs());
    }

    [Fact]
    public void HasOtherTabsInGroup_CountsCurrentOpenUngroupedMembersOnly()
    {
        var groupId = LogTabGroupId.Create();
        var present = EventLogId.Create();
        var absent = EventLogId.Create();
        var state = new LogTableState
        {
            EventTables = [new LogView(present) { LogName = "Present" }],
            Groups = [new LogTabGroup(groupId, "Group", ImmutableHashSet.Create(present, absent))]
        };

        Assert.False(new LogTableQueries(StateReturning(state)).HasOtherTabsInGroup(groupId, present));
    }

    [Fact]
    public void HasOtherTabsInGroup_WhenGroupMissing_IsFalse() =>
        Assert.False(new LogTableQueries(StateReturning(new LogTableState()))
            .HasOtherTabsInGroup(LogTabGroupId.Create(), EventLogId.Create()));

    [Fact]
    public void HasOtherTabsInGroup_WhenTwoOpenUngroupedMembers_IsTrue()
    {
        var groupId = LogTabGroupId.Create();
        var keep = EventLogId.Create();
        var other = EventLogId.Create();
        var state = new LogTableState
        {
            EventTables =
            [
                new LogView(keep) { LogName = "Keep" },
                new LogView(other) { LogName = "Other" }
            ],
            Groups = [new LogTabGroup(groupId, "Group", ImmutableHashSet.Create(keep, other))]
        };

        Assert.True(new LogTableQueries(StateReturning(state)).HasOtherTabsInGroup(groupId, keep));
    }

    [Fact]
    public void HasTabGroup_ReflectsWhetherTheGroupExists()
    {
        var groupId = LogTabGroupId.Create();
        var state = new LogTableState
        {
            Groups = [new LogTabGroup(groupId, "Group", ImmutableHashSet<EventLogId>.Empty)]
        };
        var queries = new LogTableQueries(StateReturning(state));

        Assert.True(queries.HasTabGroup(groupId));
        Assert.False(queries.HasTabGroup(LogTabGroupId.Create()));
    }

    [Fact]
    public void IsGroupDescending_ReadsCommittedDirection_NotRequested()
    {
        var state = new LogTableState { IsGroupDescending = false, RequestedIsGroupDescending = true };

        Assert.False(new LogTableQueries(StateReturning(state)).IsGroupDescending());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsGroupDescending_ReflectsTheCommittedDirection(bool descending)
    {
        var state = new LogTableState { GroupBy = ColumnName.Source, IsGroupDescending = descending };

        Assert.Equal(descending, new LogTableQueries(StateReturning(state)).IsGroupDescending());
    }

    [Fact]
    public void IsGrouping_ReadsCommittedGroupBy_NotRequested()
    {
        var state = new LogTableState { GroupBy = null, RequestedGroupBy = ColumnName.Source };

        Assert.False(new LogTableQueries(StateReturning(state)).IsGrouping());
    }

    [Fact]
    public void IsGrouping_ReflectsWhetherAColumnIsGrouped()
    {
        Assert.False(new LogTableQueries(StateReturning(new LogTableState { GroupBy = null })).IsGrouping());
        Assert.True(new LogTableQueries(StateReturning(new LogTableState { GroupBy = ColumnName.Source })).IsGrouping());
    }

    [Fact]
    public void IsTabOpen_ReflectsWhetherTheTabIsOpen()
    {
        var open = EventLogId.Create();
        var state = new LogTableState { EventTables = [new LogView(open) { LogName = "Open" }] };
        var queries = new LogTableQueries(StateReturning(state));

        Assert.True(queries.IsTabOpen(open));
        Assert.False(queries.IsTabOpen(EventLogId.Create()));
    }

    [Fact]
    public void IsUngroupedTabOpen_WhenTabHasAGroupId_IsFalse()
    {
        var tab = EventLogId.Create();
        var state = new LogTableState
        {
            EventTables = [new LogView(tab) { LogName = "Header", GroupId = LogTabGroupId.Create() }]
        };

        Assert.False(new LogTableQueries(StateReturning(state)).IsUngroupedTabOpen(tab));
    }

    [Fact]
    public void IsUngroupedTabOpen_WhenTabIsOpenAndUngrouped_IsTrue()
    {
        var tab = EventLogId.Create();
        var state = new LogTableState { EventTables = [new LogView(tab) { LogName = "Tab" }] };

        Assert.True(new LogTableQueries(StateReturning(state)).IsUngroupedTabOpen(tab));
    }

    private static IState<LogTableState> StateReturning(LogTableState state)
    {
        var stateMock = Substitute.For<IState<LogTableState>>();
        stateMock.Value.Returns(state);

        return stateMock;
    }
}
