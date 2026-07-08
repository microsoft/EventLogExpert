// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Eventing.Tests.Common.Events;

public sealed class EventLocatorTests
{
    [Fact]
    public void Locators_DifferingByLog_AreNotEqual()
    {
        // Composite identity so a combined multi-log view cannot collide two logs' row indices.
        Assert.NotEqual(new EventLocator(EventLogId.Create(), 0, 5), new EventLocator(EventLogId.Create(), 0, 5));
    }

    [Fact]
    public void Locators_DifferingOnlyByGeneration_AreNotEqual()
    {
        var logId = EventLogId.Create();

        // Stale (pre-reload) and fresh locators must stay distinct so selection does not collapse them.
        Assert.NotEqual(new EventLocator(logId, 0, 5), new EventLocator(logId, 1, 5));
    }

    [Fact]
    public void Locators_WithSameLogGenerationIndex_AreEqual()
    {
        var logId = EventLogId.Create();

        Assert.Equal(new EventLocator(logId, 3, 7), new EventLocator(logId, 3, 7));
    }
}
