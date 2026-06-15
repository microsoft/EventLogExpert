// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Tests.TestUtils.Constants;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Filtering.TestUtils.Constants;

namespace EventLogExpert.Filtering.Tests.EventData;

public sealed class EventLogDataQueryExtensionsTests
{
    [Fact]
    public void EventLogData_GetEventValues_ForId_ShouldReturnDistinctIds()
    {
        // Arrange
        var events = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(100),
            FilterEventBuilder.CreateTestEvent(100),
            FilterEventBuilder.CreateTestEvent(200)
        };

        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, events);

        // Act
        var values = logData.GetEventValues(EventProperty.Id).ToList();

        // Assert
        Assert.Equal(2, values.Count);
        Assert.Contains(FilterTestConstants.FilterValue100, values);
        Assert.Contains(FilterTestConstants.FilterValue200, values);
    }

    [Fact]
    public void EventLogData_GetEventValues_ForLevel_ShouldReturnAllSeverityLevels()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, []);

        // Act
        var values = logData.GetEventValues(EventProperty.Level).ToList();

        // Assert
        Assert.Equal(Enum.GetNames<SeverityLevel>().Length, values.Count);
    }

    [Fact]
    public void EventLogData_GetEventValues_ForLogName_ShouldReturnPerEventChannelsNotTheDataName()
    {
        // Values come from each event's LogName (the channel on the record), not EventLogData.Name (the file
        // path / descriptor) — so an .evtx import whose Name differs from the channel still yields the channels.
        var events = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(100, logName: "Security"),
            FilterEventBuilder.CreateTestEvent(200, logName: "Security"),
            FilterEventBuilder.CreateTestEvent(300, logName: "Application")
        };

        var logData = new EventLogData("saved-export.evtx", LogPathType.File, events);

        // Act
        var values = logData.GetEventValues(EventProperty.LogName).ToList();

        // Assert
        Assert.Equal(2, values.Count);
        Assert.Contains("Security", values);
        Assert.Contains("Application", values);
        Assert.DoesNotContain("saved-export.evtx", values);
    }

    [Fact]
    public void EventLogData_GetEventValues_ForSource_ShouldReturnDistinctSources()
    {
        // Arrange
        var events = new List<ResolvedEvent>
        {
            FilterEventBuilder.CreateTestEvent(100),
            FilterEventBuilder.CreateTestEvent(200),
            FilterEventBuilder.CreateTestEvent(300, FilterTestConstants.EventSourceOtherSource)
        };

        var logData = new EventLogData(Constants.LogNameTestLog, LogPathType.Channel, events);

        // Act
        var values = logData.GetEventValues(EventProperty.Source).ToList();

        // Assert
        Assert.Equal(2, values.Count);
    }

    [Fact]
    public void EventLogData_GetEventValues_ForUnknownField_ShouldReturnEmpty()
    {
        // Arrange
        var logData = new EventLogData(Constants.LogNameTestLog,
            LogPathType.Channel,
            [FilterEventBuilder.CreateTestEvent(100)]);

        // Act
        var values = logData.GetEventValues((EventProperty)999).ToList();

        // Assert
        Assert.Empty(values);
    }
}
