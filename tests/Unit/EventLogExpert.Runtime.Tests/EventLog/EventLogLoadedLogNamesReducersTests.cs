// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Runtime.EventLog;

namespace EventLogExpert.Runtime.Tests.EventLog;

public sealed class EventLogLoadedLogNamesReducersTests
{
    private const string ChannelName = "Application";
    private const string FileA = @"C:\logs\A.evtx";
    private const string FileB = @"C:\logs\B.evtx";

    [Fact]
    public void ReduceCloseAll_ClearsNamesByLogAndLoadedLogNames()
    {
        var state = Reducers.ReduceOpenLog(new EventLogState(), new OpenLogAction(ChannelName, LogPathType.Channel));
        state = Reducers.ReduceOpenLog(state, new OpenLogAction("System", LogPathType.Channel));

        state = Reducers.ReduceCloseAll(state);

        Assert.Empty(state.NamesByLog);
        Assert.Empty(state.LoadedLogNames);
    }

    [Fact]
    public void ReduceCloseLog_WithTwoChannels_RetainsRemainingLogName()
    {
        var state = Reducers.ReduceOpenLog(new EventLogState(), new OpenLogAction("System", LogPathType.Channel));
        state = Reducers.ReduceOpenLog(state, new OpenLogAction(ChannelName, LogPathType.Channel));

        state = Reducers.ReduceCloseLog(state, new CloseLogAction(state.OpenLogs[ChannelName].Id, ChannelName));

        Assert.Contains("System", state.LoadedLogNames);
        Assert.DoesNotContain(ChannelName, state.LoadedLogNames);
        Assert.Single(state.LoadedLogNames);
    }

    [Fact]
    public void ReduceLoadEvents_Channel_IsNoOpForNamesByLog()
    {
        var state = Reducers.ReduceOpenLog(new EventLogState(), new OpenLogAction(ChannelName, LogPathType.Channel));
        var logData = new EventLogData(ChannelName, LogPathType.Channel) { Id = state.OpenLogs[ChannelName].Id };
        var priorNamesByLog = state.NamesByLog;
        var priorLoadedLogNames = state.LoadedLogNames;

        var newState = Reducers.ReduceLoadEvents(
            state,
            new LoadEventsAction(logData, EventsWithLogNames("Security")));

        Assert.Same(priorNamesByLog, newState.NamesByLog);
        Assert.Same(priorLoadedLogNames, newState.LoadedLogNames);
    }

    [Fact]
    public void ReduceLoadEvents_File_ExcludesEmptyLogNames()
    {
        var state = Reducers.ReduceOpenLog(new EventLogState(), new OpenLogAction(FileA, LogPathType.File));
        var logData = new EventLogData(FileA, LogPathType.File) { Id = state.OpenLogs[FileA].Id };

        state = Reducers.ReduceLoadEvents(
            state,
            new LoadEventsAction(logData, EventsWithLogNames("Security", string.Empty, "Setup")));

        Assert.Equal(["Security", "Setup"], state.NamesByLog[FileA].OrderBy(name => name, StringComparer.Ordinal));
        Assert.DoesNotContain(string.Empty, state.LoadedLogNames);
    }

    [Fact]
    public void ReduceLoadEvents_File_ReplacesPerLogNamesAndRecomputesUnion()
    {
        var state = Reducers.ReduceOpenLog(new EventLogState(), new OpenLogAction(FileA, LogPathType.File));
        var logData = new EventLogData(FileA, LogPathType.File) { Id = state.OpenLogs[FileA].Id };

        state = Reducers.ReduceLoadEvents(state, new LoadEventsAction(logData, EventsWithLogNames("Security", "Setup")));

        Assert.Equal(["Security", "Setup"], state.NamesByLog[FileA].OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(["Security", "Setup"], state.LoadedLogNames.OrderBy(name => name, StringComparer.Ordinal));

        state = Reducers.ReduceLoadEvents(state, new LoadEventsAction(logData, EventsWithLogNames("Security")));

        Assert.Equal(["Security"], state.NamesByLog[FileA]);
        Assert.Equal(["Security"], state.LoadedLogNames);
    }

    [Fact]
    public void ReduceLoadEvents_WhenLogIdStale_LeavesLoadedLogNamesUnchanged()
    {
        var state = Reducers.ReduceOpenLog(new EventLogState(), new OpenLogAction(FileA, LogPathType.File));
        var priorLoadedLogNames = state.LoadedLogNames;
        var priorNamesByLog = state.NamesByLog;

        var staleLogData = new EventLogData(FileA, LogPathType.File);

        var newState = Reducers.ReduceLoadEvents(
            state,
            new LoadEventsAction(staleLogData, EventsWithLogNames("Security")));

        Assert.Same(priorLoadedLogNames, newState.LoadedLogNames);
        Assert.Same(priorNamesByLog, newState.NamesByLog);
    }

    [Fact]
    public void ReduceLoadEvents_WhenUnionUnchanged_PreservesLoadedLogNamesReference()
    {
        var state = Reducers.ReduceOpenLog(new EventLogState(), new OpenLogAction(FileA, LogPathType.File));
        state = Reducers.ReduceOpenLog(state, new OpenLogAction(FileB, LogPathType.File));

        var logA = new EventLogData(FileA, LogPathType.File) { Id = state.OpenLogs[FileA].Id };
        var logB = new EventLogData(FileB, LogPathType.File) { Id = state.OpenLogs[FileB].Id };

        state = Reducers.ReduceLoadEvents(state, new LoadEventsAction(logA, EventsWithLogNames("A", "B")));
        state = Reducers.ReduceLoadEvents(state, new LoadEventsAction(logB, EventsWithLogNames("B")));

        var unionBefore = state.LoadedLogNames;
        var namesAByLogBefore = state.NamesByLog[FileA];

        state = Reducers.ReduceLoadEvents(state, new LoadEventsAction(logA, EventsWithLogNames("A")));

        Assert.NotSame(namesAByLogBefore, state.NamesByLog[FileA]);
        Assert.Equal(["A"], state.NamesByLog[FileA]);
        Assert.Same(unionBefore, state.LoadedLogNames);
    }

    [Fact]
    public void ReduceLoadEventsPartial_File_UnionsPerLogNames()
    {
        var state = Reducers.ReduceOpenLog(new EventLogState(), new OpenLogAction(FileA, LogPathType.File));
        var logData = new EventLogData(FileA, LogPathType.File) { Id = state.OpenLogs[FileA].Id };

        state = Reducers.ReduceLoadEvents(state, new LoadEventsAction(logData, EventsWithLogNames("Security")));
        state = Reducers.ReduceLoadEventsPartial(state, new LoadEventsPartialAction(logData, EventsWithLogNames("Setup")));

        Assert.Equal(["Security", "Setup"], state.NamesByLog[FileA].OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(["Security", "Setup"], state.LoadedLogNames.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void ReduceOpenLog_AfterClose_ReopensWithSeededNames()
    {
        var state = Reducers.ReduceOpenLog(new EventLogState(), new OpenLogAction(ChannelName, LogPathType.Channel));
        state = Reducers.ReduceCloseLog(state, new CloseLogAction(state.OpenLogs[ChannelName].Id, ChannelName));

        Assert.Empty(state.LoadedLogNames);

        state = Reducers.ReduceOpenLog(state, new OpenLogAction(ChannelName, LogPathType.Channel));

        Assert.Contains(ChannelName, state.LoadedLogNames);
    }

    [Fact]
    public void ReduceOpenLog_Channel_SeedsLoadedLogNamesWithChannelName()
    {
        var state = Reducers.ReduceOpenLog(new EventLogState(), new OpenLogAction(ChannelName, LogPathType.Channel));

        Assert.Contains(ChannelName, state.LoadedLogNames);
        Assert.Single(state.LoadedLogNames);
    }

    [Fact]
    public void ReduceOpenLog_File_SeedsEmptyPerLogSet_LoadedLogNamesEmpty()
    {
        var state = Reducers.ReduceOpenLog(new EventLogState(), new OpenLogAction(FileA, LogPathType.File));

        Assert.True(state.NamesByLog.ContainsKey(FileA));
        Assert.Empty(state.NamesByLog[FileA]);
        Assert.Empty(state.LoadedLogNames);
    }

    private static IReadOnlyList<ResolvedEvent> EventsWithLogNames(params string[] logNames)
    {
        var events = new List<ResolvedEvent>();
        int id = 1;

        foreach (var logName in logNames)
        {
            events.Add(FilterEventBuilder.CreateTestEvent(id++, logName: logName));
        }

        return events;
    }
}
