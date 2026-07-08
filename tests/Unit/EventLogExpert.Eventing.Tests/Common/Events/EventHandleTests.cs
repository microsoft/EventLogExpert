// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Eventing.Tests.Common.Events;

public sealed class EventHandleTests
{
    [Fact]
    public void Handles_DifferingByLog_AreNotEqual()
    {
        // Composite identity so a combined multi-log view cannot collide two logs' row indices.
        Assert.NotEqual(new EventHandle(EventLogId.Create(), 0, 5), new EventHandle(EventLogId.Create(), 0, 5));
    }

    [Fact]
    public void Handles_DifferingOnlyByGeneration_AreNotEqual()
    {
        var logId = EventLogId.Create();

        // Stale (pre-reload) and fresh handles must stay distinct so selection does not collapse them.
        Assert.NotEqual(new EventHandle(logId, 0, 5), new EventHandle(logId, 1, 5));
    }

    [Fact]
    public void Handles_WithSameLogGenerationIndex_AreEqual()
    {
        var logId = EventLogId.Create();

        Assert.Equal(new EventHandle(logId, 3, 7), new EventHandle(logId, 3, 7));
    }
}
