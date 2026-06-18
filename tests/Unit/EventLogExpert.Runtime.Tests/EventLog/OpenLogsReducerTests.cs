// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Runtime.EventLog;

namespace EventLogExpert.Runtime.Tests.EventLog;

public sealed class OpenLogsReducerTests
{
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
    public void ReduceOpenLog_SeedsOpenLogsWithCorrectIdAndType()
    {
        var state = Reducers.ReduceOpenLog(new EventLogState(), new OpenLogAction("Application", LogPathType.Channel));

        Assert.True(state.OpenLogs.ContainsKey("Application"));
        Assert.Equal(LogPathType.Channel, state.OpenLogs["Application"].Type);
        Assert.NotEqual(default, state.OpenLogs["Application"].Id);
        Assert.True(state.IsLogOpen("Application"));
    }
}
