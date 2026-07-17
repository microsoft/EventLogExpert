// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Tests.LogTable.TestSupport;
using EventLogExpert.Runtime.Tests.TestUtils;
using System.Collections.Immutable;
using Reducers = EventLogExpert.Runtime.LogTable.Reducers;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class LogTabGroupTests
{
    [Fact]
    public void DisplayedEventsForTab_AllLogsHeader_ReturnsTheSameInstanceAsDisplayedEvents()
    {
        var (state, _) = SeedLogs([MakeEvent(0, 1, Time(0, 1))], [MakeEvent(1, 1, Time(1, 1))]);
        var allLogs = state.EventTables.Single(table => table.GroupId?.IsAll == true);

        Assert.True(ReferenceEquals(state.DisplayedEvents, state.DisplayedEventsForTab(allLogs)));
    }

    [Fact]
    public void DisplayedEventsForTab_GroupWithNoPresentMembers_IsEmpty()
    {
        var groupId = LogTabGroupId.Create();
        var header = new LogView(EventLogId.Create()) { GroupId = groupId, LogName = "Ghost" };
        var state = new LogTableState
        {
            Groups = ImmutableList.Create(new LogTabGroup(groupId, "Ghost", ImmutableHashSet.Create(EventLogId.Create()))),
            EventTables = ImmutableList.Create(header)
        };

        Assert.Equal(0, state.DisplayedEventsForTab(header).Count);
    }

    [Fact]
    public void DisplayedEventsForTab_SingleMemberGroup_ReturnsThatMembersList()
    {
        var (state, ids) = SeedLogs([MakeEvent(0, 1, Time(0, 1))], [MakeEvent(1, 1, Time(1, 1))]);

        var groupId = NewGroup(ref state, ids[0]);
        var header = state.EventTables.Single(table => table.GroupId == groupId);

        Assert.True(ReferenceEquals(state.EventsForLog(ids[0]), state.DisplayedEventsForTab(header)));
    }

    [Fact]
    public void DisplayedEventsForTab_StandaloneTab_ReturnsThatLogsList()
    {
        var (state, ids) = SeedLogs([MakeEvent(0, 1, Time(0, 1))]);
        var tab = state.EventTables.Single(table => table.Id == ids[0]);

        Assert.True(ReferenceEquals(state.EventsForLog(ids[0]), state.DisplayedEventsForTab(tab)));
    }

    [Fact]
    public void DisplayedEventsForTab_UserGroup_AfterAMemberListChanges_ReturnsNewInstance()
    {
        var state = TwoMemberGroup(out var groupId);
        var members = state.Groups.Single().MemberIds.ToArray();
        var before = state.DisplayedEventsForTab(state.EventTables.Single(table => table.GroupId == groupId));

        state = AppendBatch(state, members[0], MakeEvent(0, 99, Time(0, 99)));
        var after = state.DisplayedEventsForTab(state.EventTables.Single(table => table.GroupId == groupId));

        Assert.False(ReferenceEquals(before, after));
    }

    [Fact]
    public void DisplayedEventsForTab_UserGroup_AfterMembershipChanges_ReturnsNewInstance()
    {
        var log0 = new[] { MakeEvent(0, 1, Time(0, 30)) };
        var log1 = new[] { MakeEvent(1, 1, Time(1, 20)) };
        var log2 = new[] { MakeEvent(2, 1, Time(2, 10)) };
        var (state, ids) = SeedLogs(log0, log1, log2);

        var groupId = NewGroup(ref state, ids[0]);
        state = Reducers.ReduceMoveTabToGroup(state, new MoveTabToGroupAction(ids[1], groupId));
        var before = state.DisplayedEventsForTab(state.EventTables.Single(table => table.GroupId == groupId));

        state = Reducers.ReduceMoveTabToGroup(state, new MoveTabToGroupAction(ids[2], groupId));
        var after = state.DisplayedEventsForTab(state.EventTables.Single(table => table.GroupId == groupId));

        Assert.False(ReferenceEquals(before, after));
        Assert.Equal(3, after.Count);
    }

    [Fact]
    public void DisplayedEventsForTab_UserGroup_MergesOnlyItsMembers()
    {
        var log0 = new[] { MakeEvent(0, 1, Time(0, 30)), MakeEvent(0, 2, Time(0, 10)) };
        var log1 = new[] { MakeEvent(1, 1, Time(1, 20)) };
        var log2 = new[] { MakeEvent(2, 1, Time(2, 30)), MakeEvent(2, 2, Time(2, 5)) };
        var (state, ids) = SeedLogs(log0, log1, log2);

        var groupId = NewGroup(ref state, ids[0]);
        state = Reducers.ReduceMoveTabToGroup(state, new MoveTabToGroupAction(ids[2], groupId));
        var header = state.EventTables.Single(table => table.GroupId == groupId);

        var view = state.DisplayedEventsForTab(header);

        AssertViewExactly(state, view, [.. log0, .. log2]);

        var displayed = view.Slice(0, view.Count);

        foreach (var excluded in log1)
        {
            Assert.DoesNotContain(displayed, row => SameEvent(excluded, row.Lean));
        }
    }

    [Fact]
    public void DisplayedEventsForTab_UserGroup_SameGeneration_ReturnsSameInstance()
    {
        var state = TwoMemberGroup(out var groupId);
        var header = state.EventTables.Single(table => table.GroupId == groupId);

        Assert.True(ReferenceEquals(state.DisplayedEventsForTab(header), state.DisplayedEventsForTab(header)));
    }

    [Fact]
    public void GetActiveDisplayedEvents_ActiveNamedGroupHeader_ReturnsGroupMembersNotAllLogs()
    {
        var (state, ids) = SeedLogs(
            [MakeEvent(0, 1, Time(0, 1))],
            [MakeEvent(1, 1, Time(1, 1))],
            [MakeEvent(2, 1, Time(2, 1))]);

        var groupId = NewGroup(ref state, ids[0]);
        state = Reducers.ReduceMoveTabToGroup(state, new MoveTabToGroupAction(ids[1], groupId));

        var header = state.EventTables.Single(table => table.GroupId == groupId);
        state = state with { ActiveEventLogId = header.Id };

        var active = state.GetActiveDisplayedEvents();

        // Export scopes to the group's members (ids[0], ids[1]); NOT the all-logs view that also includes the
        // standalone ids[2]. Before the fix this returned DisplayedEvents (every log).
        Assert.True(ReferenceEquals(active, state.DisplayedEventsForTab(header)));
        Assert.False(ReferenceEquals(active, state.DisplayedEvents));
        Assert.Equal(2, active.Count);
    }

    [Fact]
    public void ReduceCloseAll_ClearsGroups()
    {
        var (state, ids) = SeedLogs([MakeEvent(0, 1, Time(0, 1))], [MakeEvent(1, 1, Time(1, 1))]);
        NewGroup(ref state, ids[0]);

        state = Reducers.ReduceCloseAll(state);

        Assert.Empty(state.Groups);
        Assert.Empty(state.EventTables);
    }

    [Fact]
    public void ReduceCloseLog_ClosingANonMember_KeepsTheUserGroupAndItsHeader()
    {
        var (state, ids) = SeedLogs(
            [MakeEvent(0, 1, Time(0, 1))], [MakeEvent(1, 1, Time(1, 1))], [MakeEvent(2, 1, Time(2, 1))]);
        var groupId = NewGroup(ref state, ids[0]);

        state = Reducers.ReduceCloseLog(state, new CloseLogAction(ids[2]));

        Assert.Contains(state.Groups, group => group.Id == groupId);
        Assert.Contains(state.EventTables, table => table.GroupId == groupId);
        Assert.Contains(ids[0], state.Groups.Single(group => group.Id == groupId).MemberIds);
    }

    [Fact]
    public void ReduceCloseLog_ClosingTheActiveGroupsLastMember_RepairsActive()
    {
        var (state, ids) = SeedLogs(
            [MakeEvent(0, 1, Time(0, 1))], [MakeEvent(1, 1, Time(1, 1))], [MakeEvent(2, 1, Time(2, 1))]);
        var groupId = NewGroup(ref state, ids[0]);
        var header = state.EventTables.Single(table => table.GroupId == groupId);
        state = state with { ActiveEventLogId = header.Id };

        state = Reducers.ReduceCloseLog(state, new CloseLogAction(ids[0]));

        Assert.DoesNotContain(state.EventTables, table => table.GroupId == groupId);
        Assert.Contains(state.EventTables, table => table.Id == state.ActiveEventLogId);
    }

    [Fact]
    public void ReduceCloseLog_ClosingTheGroupsLastMember_PrunesTheGroupAndHeader()
    {
        var (state, ids) = SeedLogs(
            [MakeEvent(0, 1, Time(0, 1))], [MakeEvent(1, 1, Time(1, 1))], [MakeEvent(2, 1, Time(2, 1))]);
        var groupId = NewGroup(ref state, ids[0]);

        state = Reducers.ReduceCloseLog(state, new CloseLogAction(ids[0]));

        Assert.DoesNotContain(state.Groups, group => group.Id == groupId);
        Assert.DoesNotContain(state.EventTables, table => table.GroupId == groupId);
    }

    [Fact]
    public void ReduceCloseLog_DownToOnePerLogTab_RemovesTheAllLogsHeaderButKeepsASurvivingGroup()
    {
        var (state, ids) = SeedLogs([MakeEvent(0, 1, Time(0, 1))], [MakeEvent(1, 1, Time(1, 1))]);
        var groupId = NewGroup(ref state, ids[0]);

        state = Reducers.ReduceCloseLog(state, new CloseLogAction(ids[1]));

        Assert.DoesNotContain(state.EventTables, table => table.GroupId?.IsAll == true);
        Assert.Contains(state.EventTables, table => table.GroupId == groupId);
        Assert.Single(state.EventTables, table => !table.IsCombined);
    }

    [Fact]
    public void ReduceMoveTabToGroup_AddsTheTabToTheTargetGroup()
    {
        var state = TwoMemberGroup(out var groupId);

        Assert.Equal(2, state.Groups.Single(group => group.Id == groupId).MemberIds.Count);
    }

    [Fact]
    public void ReduceMoveTabToGroup_IntoACollapsedGroup_WhenTheMovedTabIsActive_RedirectsToHeader()
    {
        var (state, ids) = SeedLogs(
            [MakeEvent(0, 1, Time(0, 1))], [MakeEvent(1, 1, Time(1, 1))], [MakeEvent(2, 1, Time(2, 1))]);
        var groupId = NewGroup(ref state, ids[0]);
        var header = state.EventTables.Single(table => table.GroupId == groupId);
        state = Reducers.ReduceSetTabGroupCollapsed(state, new SetTabGroupCollapsedAction(groupId, true));
        state = state with { ActiveEventLogId = ids[1] };

        state = Reducers.ReduceMoveTabToGroup(state, new MoveTabToGroupAction(ids[1], groupId));

        Assert.Equal(header.Id, state.ActiveEventLogId);
    }

    [Fact]
    public void ReduceMoveTabToGroup_IntoACollapsedGroup_WhenTheMovedTabIsNotActive_LeavesActiveUnchanged()
    {
        var (state, ids) = SeedLogs(
            [MakeEvent(0, 1, Time(0, 1))], [MakeEvent(1, 1, Time(1, 1))], [MakeEvent(2, 1, Time(2, 1))]);
        var groupId = NewGroup(ref state, ids[0]);
        state = Reducers.ReduceSetTabGroupCollapsed(state, new SetTabGroupCollapsedAction(groupId, true));
        state = state with { ActiveEventLogId = ids[2] };

        state = Reducers.ReduceMoveTabToGroup(state, new MoveTabToGroupAction(ids[1], groupId));

        Assert.Equal(ids[2], state.ActiveEventLogId);
    }

    [Fact]
    public void ReduceMoveTabToGroup_IntoAnExpandedGroup_KeepsTheMovedTabActive()
    {
        var (state, ids) = SeedLogs([MakeEvent(0, 1, Time(0, 1))], [MakeEvent(1, 1, Time(1, 1))]);
        var groupId = NewGroup(ref state, ids[0]);
        state = state with { ActiveEventLogId = ids[1] };

        state = Reducers.ReduceMoveTabToGroup(state, new MoveTabToGroupAction(ids[1], groupId));

        Assert.Equal(ids[1], state.ActiveEventLogId);
    }

    [Fact]
    public void ReduceMoveTabToGroup_ToTheAllLogsTarget_UngroupsTheTab()
    {
        var (state, ids) = SeedLogs([MakeEvent(0, 1, Time(0, 1))], [MakeEvent(1, 1, Time(1, 1))]);
        var groupId = NewGroup(ref state, ids[0]);

        state = Reducers.ReduceMoveTabToGroup(state, new MoveTabToGroupAction(ids[0], LogTabGroupId.AllLogs));

        Assert.Empty(state.Groups);
        Assert.DoesNotContain(state.EventTables, table => table.GroupId == groupId);
        Assert.Null(state.EventTables.Single(table => table.Id == ids[0]).GroupId);
    }

    [Fact]
    public void ReduceMoveTabToGroup_ToTheAllLogsTarget_WhenTabIsUngrouped_IsANoOp()
    {
        var (state, ids) = SeedLogs([MakeEvent(0, 1, Time(0, 1))], [MakeEvent(1, 1, Time(1, 1))]);

        var result = Reducers.ReduceMoveTabToGroup(state, new MoveTabToGroupAction(ids[0], LogTabGroupId.AllLogs));

        Assert.Same(state, result);
    }

    [Fact]
    public void ReduceMoveTabToGroup_WhenItPrunesTheActiveHeader_RepairsActive()
    {
        var (state, ids) = SeedLogs([MakeEvent(0, 1, Time(0, 1))], [MakeEvent(1, 1, Time(1, 1))]);
        var firstGroupId = NewGroup(ref state, ids[0]);
        var secondGroupId = NewGroup(ref state, ids[1]);
        var firstHeader = state.EventTables.Single(table => table.GroupId == firstGroupId);
        state = state with { ActiveEventLogId = firstHeader.Id };

        state = Reducers.ReduceMoveTabToGroup(state, new MoveTabToGroupAction(ids[0], secondGroupId));

        Assert.DoesNotContain(state.EventTables, table => table.Id == firstHeader.Id);
        Assert.Contains(state.EventTables, table => table.Id == state.ActiveEventLogId);
        Assert.Contains(ids[0], state.Groups.Single(group => group.Id == secondGroupId).MemberIds);
    }

    [Fact]
    public void ReduceMoveTabToGroup_WhenTabAlreadyInTarget_IsANoOp()
    {
        var (state, ids) = SeedLogs([MakeEvent(0, 1, Time(0, 1))], [MakeEvent(1, 1, Time(1, 1))]);
        var groupId = NewGroup(ref state, ids[0]);

        var result = Reducers.ReduceMoveTabToGroup(state, new MoveTabToGroupAction(ids[0], groupId));

        Assert.Same(state, result);
    }

    [Fact]
    public void ReduceMoveTabToGroup_WithAnUnknownTarget_IsANoOp()
    {
        var (state, ids) = SeedLogs([MakeEvent(0, 1, Time(0, 1))], [MakeEvent(1, 1, Time(1, 1))]);

        var result = Reducers.ReduceMoveTabToGroup(state, new MoveTabToGroupAction(ids[0], LogTabGroupId.Create()));

        Assert.Same(state, result);
    }

    [Fact]
    public void ReduceNewGroupFromTab_CreatesGroupAndHeader_ChildKeepsNullGroupId()
    {
        var (state, ids) = SeedLogs([MakeEvent(0, 1, Time(0, 1))], [MakeEvent(1, 1, Time(1, 1))]);

        var groupId = NewGroup(ref state, ids[0]);

        var group = state.Groups.Single();
        Assert.Equal(groupId, group.Id);
        Assert.Contains(ids[0], group.MemberIds);

        var header = state.EventTables.Single(table => table.GroupId == groupId);
        Assert.True(header.IsCombined);

        var child = state.EventTables.Single(table => table.Id == ids[0]);
        Assert.Null(child.GroupId);
        Assert.False(child.IsCombined);
    }

    [Fact]
    public void ReduceNewGroupFromTab_OnAnAlreadyGroupedTab_EnforcesSingleMembership()
    {
        var (state, ids) = SeedLogs([MakeEvent(0, 1, Time(0, 1))], [MakeEvent(1, 1, Time(1, 1))]);

        var firstGroupId = NewGroup(ref state, ids[0]);
        var secondGroupId = NewGroup(ref state, ids[0]);

        Assert.DoesNotContain(state.Groups, group => group.Id == firstGroupId);
        var group = state.Groups.Single();
        Assert.Equal(secondGroupId, group.Id);
        Assert.Single(group.MemberIds);
        Assert.Contains(ids[0], group.MemberIds);
    }

    [Fact]
    public void ReduceNewGroupFromTab_WhenItPrunesTheActiveHeader_RepairsActiveToTheNewHeader()
    {
        var (state, ids) = SeedLogs([MakeEvent(0, 1, Time(0, 1))], [MakeEvent(1, 1, Time(1, 1))]);
        var firstGroupId = NewGroup(ref state, ids[0]);
        var firstHeader = state.EventTables.Single(table => table.GroupId == firstGroupId);
        state = state with { ActiveEventLogId = firstHeader.Id };

        var secondGroupId = NewGroup(ref state, ids[0]);
        var secondHeader = state.EventTables.Single(table => table.GroupId == secondGroupId);

        Assert.DoesNotContain(state.EventTables, table => table.Id == firstHeader.Id);
        Assert.Equal(secondHeader.Id, state.ActiveEventLogId);
    }

    [Fact]
    public void ReduceRemoveTabFromGroup_OfTheLastMember_PrunesTheGroupHeaderAndRepairsActive()
    {
        var (state, ids) = SeedLogs([MakeEvent(0, 1, Time(0, 1))], [MakeEvent(1, 1, Time(1, 1))]);
        var groupId = NewGroup(ref state, ids[0]);
        var header = state.EventTables.Single(table => table.GroupId == groupId);
        state = state with { ActiveEventLogId = header.Id };

        state = Reducers.ReduceRemoveTabFromGroup(state, new RemoveTabFromGroupAction(ids[0]));

        Assert.Empty(state.Groups);
        Assert.DoesNotContain(state.EventTables, table => table.Id == header.Id);
        Assert.Contains(state.EventTables, table => table.Id == state.ActiveEventLogId);
    }

    [Fact]
    public void ReduceRenameGroup_ToTheCurrentName_IsANoOp()
    {
        var (state, ids) = SeedLogs([MakeEvent(0, 1, Time(0, 1))], [MakeEvent(1, 1, Time(1, 1))]);
        var groupId = NewGroup(ref state, ids[0]);
        var currentName = state.Groups.Single(group => group.Id == groupId).Name;

        var result = Reducers.ReduceRenameGroup(state, new RenameGroupAction(groupId, currentName));

        Assert.Same(state, result);
    }

    [Fact]
    public void ReduceRenameGroup_UpdatesBothTheGroupNameAndTheHeaderLogName()
    {
        var (state, ids) = SeedLogs([MakeEvent(0, 1, Time(0, 1))], [MakeEvent(1, 1, Time(1, 1))]);
        var groupId = NewGroup(ref state, ids[0]);

        state = Reducers.ReduceRenameGroup(state, new RenameGroupAction(groupId, "Renamed"));

        Assert.Equal("Renamed", state.Groups.Single(group => group.Id == groupId).Name);
        Assert.Equal("Renamed", state.EventTables.Single(table => table.GroupId == groupId).LogName);
    }

    [Fact]
    public void ReduceRenameGroup_WithUnknownGroup_IsANoOp()
    {
        var (state, ids) = SeedLogs([MakeEvent(0, 1, Time(0, 1))], [MakeEvent(1, 1, Time(1, 1))]);
        _ = NewGroup(ref state, ids[0]);

        var result = Reducers.ReduceRenameGroup(state, new RenameGroupAction(LogTabGroupId.Create(), "Renamed"));

        Assert.Same(state, result);
    }

    [Fact]
    public void ReduceRenameGroup_WithWhitespaceName_IsANoOp()
    {
        var (state, ids) = SeedLogs([MakeEvent(0, 1, Time(0, 1))], [MakeEvent(1, 1, Time(1, 1))]);
        var groupId = NewGroup(ref state, ids[0]);

        var result = Reducers.ReduceRenameGroup(state, new RenameGroupAction(groupId, "   "));

        Assert.Same(state, result);
    }

    [Fact]
    public void ReduceSetTabGroupCollapsed_OnExpand_DoesNotRedirectActive()
    {
        var (state, ids) = SeedLogs([MakeEvent(0, 1, Time(0, 1))], [MakeEvent(1, 1, Time(1, 1))]);
        var groupId = NewGroup(ref state, ids[0]);
        state = Reducers.ReduceSetTabGroupCollapsed(state, new SetTabGroupCollapsedAction(groupId, true));
        state = state with { ActiveEventLogId = ids[0] };

        state = Reducers.ReduceSetTabGroupCollapsed(state, new SetTabGroupCollapsedAction(groupId, false));

        Assert.Equal(ids[0], state.ActiveEventLogId);
    }

    [Fact]
    public void ReduceSetTabGroupCollapsed_SetsTheGroupsCollapsedState()
    {
        var (state, ids) = SeedLogs([MakeEvent(0, 1, Time(0, 1))], [MakeEvent(1, 1, Time(1, 1))]);
        var groupId = NewGroup(ref state, ids[0]);

        state = Reducers.ReduceSetTabGroupCollapsed(state, new SetTabGroupCollapsedAction(groupId, true));

        Assert.True(state.Groups.Single(group => group.Id == groupId).IsCollapsed);
    }

    [Fact]
    public void ReduceSetTabGroupCollapsed_WhenActiveIsNotAMember_LeavesActiveUnchanged()
    {
        var (state, ids) = SeedLogs([MakeEvent(0, 1, Time(0, 1))], [MakeEvent(1, 1, Time(1, 1))]);
        var groupId = NewGroup(ref state, ids[0]);
        state = state with { ActiveEventLogId = ids[1] };

        state = Reducers.ReduceSetTabGroupCollapsed(state, new SetTabGroupCollapsedAction(groupId, true));

        Assert.Equal(ids[1], state.ActiveEventLogId);
    }

    [Fact]
    public void ReduceSetTabGroupCollapsed_WhenActiveMemberIsHidden_RedirectsActiveToHeader()
    {
        var (state, ids) = SeedLogs([MakeEvent(0, 1, Time(0, 1))], [MakeEvent(1, 1, Time(1, 1))]);
        var groupId = NewGroup(ref state, ids[0]);
        var header = state.EventTables.Single(table => table.GroupId == groupId);
        state = state with { ActiveEventLogId = ids[0] };

        state = Reducers.ReduceSetTabGroupCollapsed(state, new SetTabGroupCollapsedAction(groupId, true));

        Assert.Equal(header.Id, state.ActiveEventLogId);
    }

    [Fact]
    public void ReduceSetTabGroupCollapsed_WhenAlreadyAtTheValue_IsANoOp()
    {
        var (state, ids) = SeedLogs([MakeEvent(0, 1, Time(0, 1))], [MakeEvent(1, 1, Time(1, 1))]);
        var groupId = NewGroup(ref state, ids[0]);

        var result = Reducers.ReduceSetTabGroupCollapsed(state, new SetTabGroupCollapsedAction(groupId, false));

        Assert.Same(state, result);
    }

    [Fact]
    public void ReduceSetTabGroupCollapsed_WithUnknownGroup_IsANoOp()
    {
        var (state, ids) = SeedLogs([MakeEvent(0, 1, Time(0, 1))], [MakeEvent(1, 1, Time(1, 1))]);
        _ = NewGroup(ref state, ids[0]);

        var result = Reducers.ReduceSetTabGroupCollapsed(state, new SetTabGroupCollapsedAction(LogTabGroupId.Create(), true));

        Assert.Same(state, result);
    }

    private static LogTableState AppendBatch(LogTableState state, EventLogId logId, params ResolvedEvent[] events) =>
        Reducers.ReduceAppendTableEventsBatch(state, new AppendTableEventsBatchAction
        {
            ViewsByLog = new Dictionary<EventLogId, EventColumnView>
            {
                [logId] = DisplayViewTestFactory.Build(logId, events)
            }
        });

    private static void AssertViewExactly(
        LogTableState state, IEventColumnView view, IReadOnlyList<ResolvedEvent> expected)
    {
        var oracle = AosReferenceOrdering.OrderedEvents(
            expected,
            ResolvedEventOrdering.ResolveDefaultOrderBy(state.OrderBy, state.GroupBy, state.PerLogEvents.Count, state.TimelineVisible),
            state.IsDescending,
            state.GroupBy,
            state.IsGroupDescending);

        Assert.Equal(oracle.Count, view.Count);

        // The view rehydrates fresh ResolvedEvent objects from columns, so compare by value identity rather than
        // reference (the AoS oracle holds the original object graph).
        var displayed = view.Slice(0, view.Count);

        for (int i = 0; i < oracle.Count; i++)
        {
            Assert.True(SameEvent(oracle[i], displayed[i].Lean), $"Order mismatch at index {i}.");
        }
    }

    private static ResolvedEvent MakeEvent(int logIndex, long? recordId, DateTime time) =>
        new($"Log{logIndex}", LogPathType.Channel) { RecordId = recordId, TimeCreated = time, Level = "Information" };

    private static LogTabGroupId NewGroup(ref LogTableState state, EventLogId tabId)
    {
        state = Reducers.ReduceNewGroupFromTab(state, new NewGroupFromTabAction(tabId, "Group"));

        return state.Groups.Single(group => group.MemberIds.Contains(tabId)).Id;
    }

    private static bool SameEvent(ResolvedEvent expected, ResolvedEvent actual) =>
        expected.RecordId == actual.RecordId
        && expected.Id == actual.Id
        && expected.TimeCreated == actual.TimeCreated
        && string.Equals(expected.Level, actual.Level, StringComparison.Ordinal);

    private static (LogTableState State, EventLogId[] Ids) SeedLogs(params IReadOnlyList<ResolvedEvent>[] perLog)
    {
        var state = new LogTableState();
        var ids = new EventLogId[perLog.Length];
        var batch = new Dictionary<EventLogId, EventColumnView>(perLog.Length);

        for (int i = 0; i < perLog.Length; i++)
        {
            var data = new EventLogData($"Log{i}", LogPathType.Channel);
            ids[i] = data.Id;
            state = Reducers.ReduceAddTable(state, new AddTableAction(data));
            batch[data.Id] = DisplayViewTestFactory.Build(data.Id, perLog[i]);
        }

        state = Reducers.ReduceAppendTableEventsBatch(state, new AppendTableEventsBatchAction { ViewsByLog = batch });

        return (state, ids);
    }

    private static DateTime Time(int logIndex, int seconds) =>
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(logIndex).AddSeconds(seconds);

    private static LogTableState TwoMemberGroup(out LogTabGroupId groupId)
    {
        var (state, ids) = SeedLogs(
            [MakeEvent(0, 1, Time(0, 30)), MakeEvent(0, 2, Time(0, 10))],
            [MakeEvent(1, 1, Time(1, 20)), MakeEvent(1, 2, Time(1, 5))]);

        groupId = NewGroup(ref state, ids[0]);
        state = Reducers.ReduceMoveTabToGroup(state, new MoveTabToGroupAction(ids[1], groupId));

        return state;
    }
}
