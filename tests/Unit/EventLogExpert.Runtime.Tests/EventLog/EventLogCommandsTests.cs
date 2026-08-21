// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Runtime.EventLog;
using NSubstitute;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Runtime.Tests.EventLog;

public sealed class EventLogCommandsTests
{
    [Fact]
    public void CloseLog_DispatchesUserInitiatedCloseActionAndUserMarker()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var commands = new EventLogCommands(dispatcher);
        var logId = EventLogId.Create();

        commands.CloseLog(logId, "Application");

        dispatcher.Received(1).Dispatch(Arg.Is<CloseLogAction>(action =>
            action != null && action.LogId == logId && action.LogName == "Application" && action.UserInitiated));
        dispatcher.Received(1).Dispatch(Arg.Is<LogClosedByUserAction>(action =>
            action != null && action.LogId == logId && action.LogName == "Application"));
    }
}
