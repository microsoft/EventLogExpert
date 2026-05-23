// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Runtime.Tests.TestUtils.Constants;

namespace EventLogExpert.Runtime.Tests.EventLog;

public sealed class EventLogDataTests
{
    [Fact]
    public void Id_CreateFactory_AlwaysProducesNonDefault()
    {
        // Arrange + Act
        var id = EventLogId.Create();

        // Assert
        Assert.NotEqual(default, id);
        Assert.NotEqual(Guid.Empty, id.Value);
    }

    [Fact]
    public void New_DistinctInstances_GetDistinctIds()
    {
        // Arrange + Act
        var a = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);
        var b = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);

        // Assert
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void With_ChainedCopies_PreservesOriginalId()
    {
        // Arrange
        var original = new EventLogData(
            Constants.LogNameTestLog,
            LogPathType.Channel,
            [FilterEventBuilder.CreateTestEvent()]);

        // Act
        var step1 = original with { Name = Constants.LogNameLog2 };
        var step2 = step1 with { Events = new List<ResolvedEvent> { FilterEventBuilder.CreateTestEvent(2) }.AsReadOnly() };
        var step3 = step2 with { Type = LogPathType.File };

        // Assert
        Assert.Equal(original.Id, step1.Id);
        Assert.Equal(original.Id, step2.Id);
        Assert.Equal(original.Id, step3.Id);
    }

    [Fact]
    public void With_ChangingEvents_PreservesId()
    {
        // Arrange
        var original = new EventLogData(
            Constants.LogNameTestLog,
            LogPathType.Channel,
            [FilterEventBuilder.CreateTestEvent()]);

        var newEvents = new List<ResolvedEvent> { FilterEventBuilder.CreateTestEvent(2) };

        // Act
        var mutated = original with { Events = newEvents.AsReadOnly() };

        // Assert
        Assert.Equal(original.Id, mutated.Id);
        Assert.NotSame(original.Events, mutated.Events);
    }

    [Fact]
    public void With_ChangingName_PreservesId()
    {
        // Arrange
        var original = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);

        // Act
        var mutated = original with { Name = Constants.LogNameLog2 };

        // Assert
        Assert.Equal(original.Id, mutated.Id);
        Assert.NotEqual(original.Name, mutated.Name);
    }

    [Fact]
    public void With_ChangingType_PreservesId()
    {
        // Arrange
        var original = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);

        // Act
        var mutated = original with { Type = LogPathType.File };

        // Assert
        Assert.Equal(original.Id, mutated.Id);
        Assert.NotEqual(original.Type, mutated.Type);
    }
}
