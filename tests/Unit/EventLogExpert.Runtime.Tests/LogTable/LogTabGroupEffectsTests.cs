// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.LogTable;
using Fluxor;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class LogTabGroupEffectsTests
{
    [Fact]
    public async Task HandleCloseAllButThis_ClosesEveryOtherLogTab_ButNotTheAnchorOrHeaders()
    {
        var groupId = LogTabGroupId.Create();
        var anchor = new LogView(EventLogId.Create()) { LogName = "Application" };
        var grouped = new LogView(EventLogId.Create()) { LogName = "Security" };
        var standalone = new LogView(EventLogId.Create()) { LogName = "System" };
        var header = new LogView(EventLogId.Create()) { GroupId = groupId, LogName = "Group" };
        var state = new LogTableState
        {
            EventTables = [header, anchor, grouped, standalone],
            Groups = [new LogTabGroup(groupId, "Group", [grouped.Id])]
        };
        var (effects, commands, dispatcher) = CreateEffects(state);

        await effects.HandleCloseAllButThis(new CloseAllButThisAction(anchor.Id), dispatcher);

        commands.Received(1).CloseLog(grouped.Id, "Security");
        commands.Received(1).CloseLog(standalone.Id, "System");
        commands.DidNotReceive().CloseLog(anchor.Id, Arg.Any<string>());
        commands.DidNotReceive().CloseLog(header.Id, Arg.Any<string>());
    }

    [Fact]
    public async Task HandleCloseAllButThis_WhenTabIsAGroupHeader_ClosesNothing()
    {
        var groupId = LogTabGroupId.Create();
        var member = new LogView(EventLogId.Create()) { LogName = "Application" };
        var header = new LogView(EventLogId.Create()) { GroupId = groupId, LogName = "Group" };
        var state = new LogTableState
        {
            EventTables = [header, member],
            Groups = [new LogTabGroup(groupId, "Group", [member.Id])]
        };
        var (effects, commands, dispatcher) = CreateEffects(state);

        await effects.HandleCloseAllButThis(new CloseAllButThisAction(header.Id), dispatcher);

        commands.DidNotReceive().CloseLog(Arg.Any<EventLogId>(), Arg.Any<string>());
    }

    [Fact]
    public async Task HandleCloseGroup_ClosesEveryMemberOfTheGroup()
    {
        var groupId = LogTabGroupId.Create();
        var first = new LogView(EventLogId.Create()) { LogName = "Application" };
        var second = new LogView(EventLogId.Create()) { LogName = "Security" };
        var header = new LogView(EventLogId.Create()) { GroupId = groupId, LogName = "Group" };
        var state = new LogTableState
        {
            EventTables = [header, first, second],
            Groups = [new LogTabGroup(groupId, "Group", [first.Id, second.Id])]
        };
        var (effects, commands, dispatcher) = CreateEffects(state);

        await effects.HandleCloseGroup(new CloseGroupAction(groupId), dispatcher);

        commands.Received(1).CloseLog(first.Id, "Application");
        commands.Received(1).CloseLog(second.Id, "Security");
    }

    [Fact]
    public async Task HandleCloseGroup_WithAMissingMemberTab_ClosesOnlyThePresentMembers()
    {
        var groupId = LogTabGroupId.Create();
        var present = new LogView(EventLogId.Create()) { LogName = "Application" };
        var missingId = EventLogId.Create();
        var header = new LogView(EventLogId.Create()) { GroupId = groupId, LogName = "Group" };
        var state = new LogTableState
        {
            EventTables = [header, present],
            Groups = [new LogTabGroup(groupId, "Group", [present.Id, missingId])]
        };
        var (effects, commands, dispatcher) = CreateEffects(state);

        await effects.HandleCloseGroup(new CloseGroupAction(groupId), dispatcher);

        commands.Received(1).CloseLog(present.Id, "Application");
        commands.DidNotReceive().CloseLog(missingId, Arg.Any<string>());
    }

    [Fact]
    public async Task HandleCloseGroup_WithUnknownGroup_ClosesNothing()
    {
        var present = new LogView(EventLogId.Create()) { LogName = "Application" };
        var state = new LogTableState { EventTables = [present] };
        var (effects, commands, dispatcher) = CreateEffects(state);

        await effects.HandleCloseGroup(new CloseGroupAction(LogTabGroupId.Create()), dispatcher);

        commands.DidNotReceive().CloseLog(Arg.Any<EventLogId>(), Arg.Any<string>());
    }

    [Fact]
    public async Task HandleCloseOthersInGroup_ClosesEveryMemberExceptTheKeptTab()
    {
        var groupId = LogTabGroupId.Create();
        var kept = new LogView(EventLogId.Create()) { LogName = "Application" };
        var other = new LogView(EventLogId.Create()) { LogName = "Security" };
        var header = new LogView(EventLogId.Create()) { GroupId = groupId, LogName = "Group" };
        var state = new LogTableState
        {
            EventTables = [header, kept, other],
            Groups = [new LogTabGroup(groupId, "Group", [kept.Id, other.Id])]
        };
        var (effects, commands, dispatcher) = CreateEffects(state);

        await effects.HandleCloseOthersInGroup(new CloseOthersInGroupAction(groupId, kept.Id), dispatcher);

        commands.Received(1).CloseLog(other.Id, "Security");
        commands.DidNotReceive().CloseLog(kept.Id, Arg.Any<string>());
    }

    [Fact]
    public async Task HandleCloseOthersInGroup_WhenKeptTabIsNotInTheGroup_ClosesNothing()
    {
        var groupId = LogTabGroupId.Create();
        var member = new LogView(EventLogId.Create()) { LogName = "Application" };
        var outsider = new LogView(EventLogId.Create()) { LogName = "Security" };
        var header = new LogView(EventLogId.Create()) { GroupId = groupId, LogName = "Group" };
        var state = new LogTableState
        {
            EventTables = [header, member, outsider],
            Groups = [new LogTabGroup(groupId, "Group", [member.Id])]
        };
        var (effects, commands, dispatcher) = CreateEffects(state);

        await effects.HandleCloseOthersInGroup(new CloseOthersInGroupAction(groupId, outsider.Id), dispatcher);

        commands.DidNotReceive().CloseLog(Arg.Any<EventLogId>(), Arg.Any<string>());
    }

    private static (LogTabGroupEffects Effects, IEventLogCommands Commands, IDispatcher Dispatcher) CreateEffects(
        LogTableState state)
    {
        var logTableState = Substitute.For<IState<LogTableState>>();
        logTableState.Value.Returns(state);

        var commands = Substitute.For<IEventLogCommands>();
        var effects = new LogTabGroupEffects(logTableState, commands);

        return (effects, commands, Substitute.For<IDispatcher>());
    }
}
