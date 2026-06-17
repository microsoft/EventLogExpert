// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Runtime.EventLog;

namespace EventLogExpert.Runtime.Tests.EventLog;

public sealed class OpenLogsReducerTests
{
    [Fact]
    public void OpenLogs_KeysAndMetadataMirrorActiveLogs_AcrossOpenCloseSequence()
    {
        var state = new EventLogState();
        state = Reducers.ReduceOpenLog(state, new OpenLogAction("Application", LogPathType.Channel));
        state = Reducers.ReduceOpenLog(state, new OpenLogAction("C:/logs/security.evtx", LogPathType.File));
        state = Reducers.ReduceOpenLog(state, new OpenLogAction("System", LogPathType.Channel));

        var systemId = state.OpenLogs["System"].Id;
        state = Reducers.ReduceCloseLog(state, new CloseLogAction(systemId, "System"));

        AssertOpenLogsMirrorActiveLogs(state);
    }

    [Fact]
    public void ReduceCloseAll_ClearsOpenLogs()
    {
        var state = Reducers.ReduceOpenLog(new EventLogState(), new OpenLogAction("Application", LogPathType.Channel));
        state = Reducers.ReduceOpenLog(state, new OpenLogAction("System", LogPathType.Channel));

        state = Reducers.ReduceCloseAll(state);

        Assert.Empty(state.OpenLogs);
    }

    [Fact]
    public void ReduceCloseLog_RemovesTheOpenLogEntry()
    {
        var state = Reducers.ReduceOpenLog(new EventLogState(), new OpenLogAction("Application", LogPathType.Channel));
        var id = state.OpenLogs["Application"].Id;

        state = Reducers.ReduceCloseLog(state, new CloseLogAction(id, "Application"));

        Assert.False(state.OpenLogs.ContainsKey("Application"));
        Assert.False(state.IsLogOpen("Application"));
    }

    [Fact]
    public void ReduceOpenLog_ReopeningSameName_LeavesOpenLogsUnchanged()
    {
        var state = Reducers.ReduceOpenLog(new EventLogState(), new OpenLogAction("Application", LogPathType.Channel));
        var seededId = state.OpenLogs["Application"].Id;

        state = Reducers.ReduceOpenLog(state, new OpenLogAction("Application", LogPathType.Channel));

        Assert.Single(state.OpenLogs);
        Assert.Equal(seededId, state.OpenLogs["Application"].Id);
    }

    [Fact]
    public void ReduceOpenLog_SeedsOpenLogsWithSameIdAndTypeAsActiveLogs()
    {
        var state = Reducers.ReduceOpenLog(new EventLogState(), new OpenLogAction("Application", LogPathType.Channel));

        Assert.True(state.OpenLogs.ContainsKey("Application"));
        Assert.Equal(LogPathType.Channel, state.OpenLogs["Application"].Type);
        Assert.Equal(state.ActiveLogs["Application"].Id, state.OpenLogs["Application"].Id);
        Assert.True(state.IsLogOpen("Application"));
    }

    private static void AssertOpenLogsMirrorActiveLogs(EventLogState state)
    {
        Assert.Equal(
            state.ActiveLogs.Keys.OrderBy(name => name),
            state.OpenLogs.Keys.OrderBy(name => name));

        foreach (var (name, logData) in state.ActiveLogs)
        {
            Assert.Equal(logData.Id, state.OpenLogs[name].Id);
            Assert.Equal(logData.Type, state.OpenLogs[name].Type);
        }
    }
}
