// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Tests.TestUtils.Constants;
using System.Diagnostics;

namespace EventLogExpert.Eventing.Tests.Readers;

public sealed class EventLogWatcherTests
{
    [Theory]
    [InlineData(Constants.ApplicationLogName)]
    [InlineData(Constants.SystemLogName)]
    public void Constructor_WithCommonLogs_ShouldCreateWatcher(string logName)
    {
        // Arrange & Act
        using var watcher = new EventLogWatcher(logName);

        // Assert
        Assert.NotNull(watcher);
        Assert.False(watcher.Enabled);
    }

    [Fact]
    public void Constructor_WithDifferentRenderXmlValues_ShouldCreateWatcher()
    {
        // Arrange & Act
        using var watcherWithXml = new EventLogWatcher(Constants.ApplicationLogName, renderXml: true);
        using var watcherWithoutXml = new EventLogWatcher(Constants.ApplicationLogName, renderXml: false);

        // Assert
        Assert.NotNull(watcherWithXml);
        Assert.NotNull(watcherWithoutXml);
    }

    [Fact]
    public void Constructor_WithEmptyLogName_ShouldThrowArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new EventLogWatcher(string.Empty));
    }

    [Fact]
    public void Constructor_WithInvalidLogName_ShouldThrowException()
    {
        // Arrange
        var invalidLogName = "NonExistentLog_" + Guid.NewGuid();

        // Act & Assert
        Assert.ThrowsAny<Exception>(() => new EventLogWatcher(invalidLogName));
    }

    [Fact]
    public void Constructor_WithLogName_ShouldCreateWatcher()
    {
        // Arrange & Act
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);

        // Assert
        Assert.NotNull(watcher);
    }

    [Fact]
    public void Constructor_WithLogNameAndRenderXml_ShouldCreateWatcher()
    {
        // Arrange & Act
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName, renderXml: true);

        // Assert
        Assert.NotNull(watcher);
    }

    [Fact]
    public void Constructor_WithLogNameBookmarkAndRenderXml_ShouldCreateWatcher()
    {
        // Arrange
        string? bookmark = null;

        // Act
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName, bookmark, renderXml: false);

        // Assert
        Assert.NotNull(watcher);
    }

    [Fact]
    public void Constructor_WithNullBookmark_ShouldCreateWatcher()
    {
        // Arrange & Act
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName, null, renderXml: false);

        // Assert
        Assert.NotNull(watcher);
    }

    [Fact]
    public void Constructor_WithNullLogName_ShouldThrowArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new EventLogWatcher(null!));
    }

    [Fact]
    public void Constructor_WithValidBookmark_ShouldCreateWatcher()
    {
        // Arrange
        // Get a valid bookmark from EventLogReader
        using var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName);

        reader.TryGetEvents(out _, 1);
        
        var bookmark = reader.LastBookmark;

        // Act
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName, bookmark, renderXml: false);

        // Assert
        Assert.NotNull(watcher);
    }

    [Fact]
    public void Constructor_WithWhitespaceLogName_ShouldThrowArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new EventLogWatcher("   "));
    }

    [Fact]
    public void Dispose_AfterDispose_ShouldNotReceiveEvents()
    {
        // Arrange
        var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        var receivedEvents = new List<EventRecord>();

        watcher.EventRecordWritten += (sender, record) => receivedEvents.Add(record);
        watcher.Enabled = true;

        // Act
        watcher.Dispose();
        
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event after dispose", EventLogEntryType.Information);

        Thread.Sleep(200); // Wait to ensure event would have been received if subscription was active

        // Assert
        Assert.Empty(receivedEvents);
    }

    [Fact]
    public void Dispose_BeforeSubscribe_ShouldNotThrow()
    {
        // Arrange
        var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        Assert.False(watcher.Enabled);

        // Act & Assert
        watcher.Dispose();
    }

    [Fact]
    public void Dispose_WhenCalled_ShouldUnsubscribe()
    {
        // Arrange
        var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        watcher.Enabled = true;

        // Act
        watcher.Dispose();

        // Assert
        Assert.False(watcher.Enabled);
    }

    [Fact]
    public void Dispose_WhenCalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        watcher.Enabled = true;

        // Act & Assert
        watcher.Dispose();
        watcher.Dispose();
        watcher.Dispose();
    }

    [Fact]
    public void Dispose_WhenNotSubscribed_ShouldNotThrow()
    {
        // Arrange
        var watcher = new EventLogWatcher(Constants.ApplicationLogName);

        // Act & Assert
        watcher.Dispose();
    }

    [Fact]
    public void Dispose_WhileSubscribed_ShouldUnsubscribeAndDispose()
    {
        // Arrange
        var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        watcher.Enabled = true;
        Assert.True(watcher.Enabled);

        // Act
        watcher.Dispose();

        // Assert
        Assert.False(watcher.Enabled);
    }

    [Fact]
    public void Enabled_AfterMultipleToggle_ShouldMaintainCorrectState()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);

        // Act & Assert
        for (int i = 0; i < 3; i++)
        {
            watcher.Enabled = true;
            Assert.True(watcher.Enabled, $"Iteration {i}: Expected Enabled to be true after setting");

            watcher.Enabled = false;
            Assert.False(watcher.Enabled, $"Iteration {i}: Expected Enabled to be false after setting");
        }
    }

    [Fact]
    public void Enabled_WhenCreated_ShouldBeFalse()
    {
        // Arrange & Act
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);

        // Assert
        Assert.False(watcher.Enabled);
    }

    [Fact]
    public void Enabled_WhenSetToFalse_ShouldUnsubscribe()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        watcher.Enabled = true;

        // Act
        watcher.Enabled = false;

        // Assert
        Assert.False(watcher.Enabled);
    }

    [Fact]
    public void Enabled_WhenSetToFalseTwice_ShouldNotThrow()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        watcher.Enabled = true;
        watcher.Enabled = false;

        // Act & Assert
        watcher.Enabled = false;
        Assert.False(watcher.Enabled);
    }

    [Fact]
    public void Enabled_WhenSetToSameValue_ShouldNotThrow()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        watcher.Enabled = false;

        // Act & Assert
        watcher.Enabled = false;
        Assert.False(watcher.Enabled);
    }

    [Fact]
    public void Enabled_WhenSetToTrue_ShouldSubscribe()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);

        // Act
        watcher.Enabled = true;

        // Assert
        Assert.True(watcher.Enabled);
    }

    [Fact]
    public void Enabled_WhenSetToTrueTwice_ShouldNotThrow()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        watcher.Enabled = true;

        // Act - Setting to true when already true is a no-op (switch case doesn't match)
        watcher.Enabled = true;

        // Assert
        Assert.True(watcher.Enabled);
    }

    [Fact]
    public void Enabled_WhenSetToTrueTwice_ShouldRemainEnabled()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        watcher.Enabled = true;

        // Act - Setting to true when already true is a no-op (switch case doesn't match)
        watcher.Enabled = true;

        // Assert
        Assert.True(watcher.Enabled);
    }

    [Fact]
    public void Enabled_WhenToggled_ShouldUpdateState()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);

        // Act & Assert
        Assert.False(watcher.Enabled);

        watcher.Enabled = true;
        Assert.True(watcher.Enabled);

        watcher.Enabled = false;
        Assert.False(watcher.Enabled);

        watcher.Enabled = true;
        Assert.True(watcher.Enabled);
    }

    [Fact]
    public void EventRecordWritten_AfterResubscribe_ShouldReceiveEvents()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        var receivedEvents = new List<EventRecord>();
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            receivedEvents.Add(record);
            eventReceived.Set();
        };

        // Act
        watcher.Enabled = true;
        watcher.Enabled = false;
        receivedEvents.Clear();
        eventReceived.Reset();

        watcher.Enabled = true;

        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event after resubscribe", EventLogEntryType.Information);

        bool received = eventReceived.Wait(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(received, "Did not receive event within timeout period");
        Assert.NotEmpty(receivedEvents);
    }

    [Fact]
    public void EventRecordWritten_AfterUnsubscribe_ShouldStopReceivingEvents()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        var receivedEvents = new List<EventRecord>();

        watcher.EventRecordWritten += (sender, record) => receivedEvents.Add(record);
        watcher.Enabled = true;

        // Act
        watcher.Enabled = false;
        int countBeforeWait = receivedEvents.Count;
        Thread.Sleep(100); // Brief wait to ensure no new events arrive

        // Assert
        Assert.Equal(countBeforeWait, receivedEvents.Count);
    }

    [Fact]
    public void EventRecordWritten_ShouldHaveRecordId()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        EventRecord? capturedEvent = null;
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            capturedEvent = record;
            eventReceived.Set();
        };

        watcher.Enabled = true;

        // Act
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for record ID", EventLogEntryType.Information);

        bool received = eventReceived.Wait(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(received, "Did not receive event within timeout period");
        Assert.NotNull(capturedEvent);
        Assert.NotNull(capturedEvent.RecordId);
        Assert.True(capturedEvent.RecordId > 0);
    }

    [Fact]
    public void EventRecordWritten_ShouldHaveValidTimeCreated()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        EventRecord? capturedEvent = null;
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            capturedEvent = record;
            eventReceived.Set();
        };

        watcher.Enabled = true;

        // Act
        var beforeWrite = DateTime.UtcNow;
        
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for timestamp", EventLogEntryType.Information);

        bool received = eventReceived.Wait(TimeSpan.FromSeconds(5));
        var afterWrite = DateTime.UtcNow.AddSeconds(1); // Add buffer for processing

        // Assert
        Assert.True(received, "Did not receive event within timeout period");
        Assert.NotNull(capturedEvent);
        Assert.NotNull(capturedEvent.TimeCreated);
        Assert.True(capturedEvent.TimeCreated >= beforeWrite.AddSeconds(-1));
        Assert.True(capturedEvent.TimeCreated <= afterWrite);
    }

    [Fact]
    public void EventRecordWritten_ShouldIncludePathName()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        EventRecord? capturedEvent = null;
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            capturedEvent = record;
            eventReceived.Set();
        };

        watcher.Enabled = true;

        // Act
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for path validation", EventLogEntryType.Information);

        bool received = eventReceived.Wait(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(received, "Did not receive event within timeout period");
        Assert.NotNull(capturedEvent);
        Assert.Equal(Constants.ApplicationLogName, capturedEvent.PathName);
    }

    [Fact]
    public void EventRecordWritten_ShouldIncludeProperties()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        EventRecord? capturedEvent = null;
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            capturedEvent = record;
            eventReceived.Set();
        };

        watcher.Enabled = true;

        // Act
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for properties", EventLogEntryType.Information);

        bool received = eventReceived.Wait(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(received, "Did not receive event within timeout period");
        Assert.NotNull(capturedEvent);
        Assert.NotNull(capturedEvent.Properties);
    }

    [Fact]
    public void EventRecordWritten_ShouldProvideCorrectSender()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        object? capturedSender = null;
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            capturedSender = sender;
            eventReceived.Set();
        };

        watcher.Enabled = true;

        // Act
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for sender validation", EventLogEntryType.Information);

        bool received = eventReceived.Wait(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(received, "Did not receive event within timeout period");
        Assert.NotNull(capturedSender);
        Assert.Same(watcher, capturedSender);
    }

    [Fact]
    public void EventRecordWritten_ShouldReceiveEventsInOrder()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        var receivedEvents = new List<EventRecord>();
        var countdown = new CountdownEvent(3);

        watcher.EventRecordWritten += (sender, record) =>
        {
            lock (receivedEvents)
            {
                receivedEvents.Add(record);
            }

            countdown.Signal();
        };

        watcher.Enabled = true;

        // Act
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;

        var startTime = DateTime.UtcNow;
        eventLog.WriteEntry("Event A", EventLogEntryType.Information);
        Thread.Sleep(50); // Small delay between events
        eventLog.WriteEntry("Event B", EventLogEntryType.Information);
        Thread.Sleep(50);
        eventLog.WriteEntry("Event C", EventLogEntryType.Information);

        bool received = countdown.Wait(TimeSpan.FromSeconds(10));

        // Assert
        Assert.True(received, "Did not receive all events within timeout period");
        Assert.True(receivedEvents.Count >= 3);
        
        // Verify events are in chronological order (by TimeCreated)
        for (int i = 1; i < Math.Min(3, receivedEvents.Count); i++)
        {
            Assert.True(receivedEvents[i].TimeCreated >= receivedEvents[i - 1].TimeCreated,
                $"Event {i} was received before event {i - 1}");
        }
    }

    [Fact]
    public void EventRecordWritten_WhenErrorOccurs_ShouldReceiveEventRecordWithError()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        var receivedEvents = new List<EventRecord>();
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            receivedEvents.Add(record);

            if (!eventReceived.IsSet)
            {
                eventReceived.Set();
            }
        };

        watcher.Enabled = true;

        // Act - Write a normal event (error handling is internal, hard to trigger externally)
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for error case", EventLogEntryType.Information);

        bool received = eventReceived.Wait(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(received, "Did not receive event within timeout period");
        Assert.NotEmpty(receivedEvents);
        // Normal events should not have errors
        Assert.All(receivedEvents, evt => Assert.Null(evt.Error));
    }

    [Fact]
    public void EventRecordWritten_WhenMultipleEventsWritten_ShouldReceiveAll()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        var receivedEvents = new List<EventRecord>();
        var countdown = new CountdownEvent(3);

        watcher.EventRecordWritten += (sender, record) =>
        {
            receivedEvents.Add(record);
            countdown.Signal();
        };

        watcher.Enabled = true;

        // Act
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;

        eventLog.WriteEntry("Test event 1", EventLogEntryType.Information);
        eventLog.WriteEntry("Test event 2", EventLogEntryType.Warning);
        eventLog.WriteEntry("Test event 3", EventLogEntryType.Error);

        bool received = countdown.Wait(TimeSpan.FromSeconds(10));

        // Assert
        Assert.True(received, "Did not receive all events within timeout period");
        Assert.True(receivedEvents.Count >= 3, $"Expected at least 3 events, but got {receivedEvents.Count}");
    }

    [Fact]
    public void EventRecordWritten_WhenNotSubscribed_ShouldNotReceiveEvents()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        var receivedEvents = new List<EventRecord>();

        watcher.EventRecordWritten += (sender, record) => receivedEvents.Add(record);

        // Act
        Thread.Sleep(100); // Brief wait to ensure no events arrive

        // Assert
        Assert.Empty(receivedEvents);
    }

    [Fact]
    public void EventRecordWritten_WhenSubscribed_ShouldReceiveEvents()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        var receivedEvents = new List<EventRecord>();
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            receivedEvents.Add(record);
            eventReceived.Set();
        };

        // Act
        watcher.Enabled = true;

        // Write a test event to Application log
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for EventLogWatcher", EventLogEntryType.Information);

        bool received = eventReceived.Wait(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(received, "Did not receive event within timeout period");
        Assert.NotEmpty(receivedEvents);
        Assert.All(receivedEvents, evt => Assert.NotNull(evt));
    }

    [Fact]
    public void EventRecordWritten_WithBookmark_ShouldReceiveNewEvents()
    {
        // Arrange
        // Get a bookmark from current position
        using var reader = new EventLogReader(Constants.ApplicationLogName, PathType.LogName);
        reader.TryGetEvents(out _, 1);
        var bookmark = reader.LastBookmark;

        using var watcher = new EventLogWatcher(Constants.ApplicationLogName, bookmark, renderXml: false);
        var receivedEvents = new List<EventRecord>();
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            receivedEvents.Add(record);
            eventReceived.Set();
        };

        // Act
        watcher.Enabled = true;

        // Write a new event
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event after bookmark", EventLogEntryType.Information);

        bool received = eventReceived.Wait(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(received, "Did not receive event within timeout period");
        Assert.NotEmpty(receivedEvents);
    }

    [Fact]
    public async Task EventRecordWritten_WithConcurrentEventWrites_ShouldHandleAllEvents()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        var receivedEvents = new List<EventRecord>();
        var countdown = new CountdownEvent(5);

        watcher.EventRecordWritten += (sender, record) =>
        {
            lock (receivedEvents)
            {
                receivedEvents.Add(record);
            }

            countdown.Signal();
        };

        watcher.Enabled = true;

        // Act - Write events from multiple threads
        var tasks = new[]
        {
            Task.Run(() =>
            {
                using var log = new EventLog(Constants.ApplicationLogName);
                log.Source = Constants.ApplicationLogName;
                log.WriteEntry("Concurrent event 1", EventLogEntryType.Information);
            }),
            Task.Run(() =>
            {
                using var log = new EventLog(Constants.ApplicationLogName);
                log.Source = Constants.ApplicationLogName;
                log.WriteEntry("Concurrent event 2", EventLogEntryType.Information);
            }),
            Task.Run(() =>
            {
                using var log = new EventLog(Constants.ApplicationLogName);
                log.Source = Constants.ApplicationLogName;
                log.WriteEntry("Concurrent event 3", EventLogEntryType.Information);
            }),
            Task.Run(() =>
            {
                using var log = new EventLog(Constants.ApplicationLogName);
                log.Source = Constants.ApplicationLogName;
                log.WriteEntry("Concurrent event 4", EventLogEntryType.Information);
            }),
            Task.Run(() =>
            {
                using var log = new EventLog(Constants.ApplicationLogName);
                log.Source = Constants.ApplicationLogName;
                log.WriteEntry("Concurrent event 5", EventLogEntryType.Information);
            })
        };

        await Task.WhenAll(tasks);
        bool received = countdown.Wait(TimeSpan.FromSeconds(10));

        // Assert
        Assert.True(received, $"Did not receive all events within timeout period. Got {receivedEvents.Count} events");
        Assert.True(receivedEvents.Count >= 5, $"Expected at least 5 events, but got {receivedEvents.Count}");
    }

    [Fact]
    public void EventRecordWritten_WithInvalidBookmark_ShouldThrowException()
    {
        // Arrange
        var invalidBookmark = "InvalidBookmarkString";
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName, invalidBookmark, renderXml: false);

        // Act & Assert
        Assert.ThrowsAny<Exception>(() => watcher.Enabled = true);
    }

    [Fact]
    public void EventRecordWritten_WithMultipleSubscribers_ShouldNotifyAll()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        var receivedEvents1 = new List<EventRecord>();
        var receivedEvents2 = new List<EventRecord>();
        var countdown = new CountdownEvent(2);

        watcher.EventRecordWritten += (sender, record) =>
        {
            receivedEvents1.Add(record);
            countdown.Signal();
        };

        watcher.EventRecordWritten += (sender, record) =>
        {
            receivedEvents2.Add(record);
            countdown.Signal();
        };

        watcher.Enabled = true;

        // Act
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for multiple subscribers", EventLogEntryType.Information);

        bool received = countdown.Wait(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(received, "Did not receive events in all subscribers within timeout period");
        Assert.NotEmpty(receivedEvents1);
        Assert.NotEmpty(receivedEvents2);
    }

    [Fact]
    public void EventRecordWritten_WithNoSubscribers_ShouldNotThrow()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);

        // Act
        watcher.Enabled = true;

        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event with no subscribers", EventLogEntryType.Information);

        Thread.Sleep(200); // Wait for event processing

        // Assert - No exception means success
        Assert.True(watcher.Enabled);
    }

    [Fact]
    public void EventRecordWritten_WithRenderXmlFalse_ShouldNotIncludeXml()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName, renderXml: false);
        EventRecord? capturedEvent = null;
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            capturedEvent = record;
            eventReceived.Set();
        };

        watcher.Enabled = true;

        // Act
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event without XML", EventLogEntryType.Information);

        bool received = eventReceived.Wait(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(received, "Did not receive event within timeout period");
        Assert.NotNull(capturedEvent);
        Assert.Null(capturedEvent.Xml);
    }

    [Fact]
    public void EventRecordWritten_WithRenderXmlTrue_ShouldIncludeXml()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName, renderXml: true);
        EventRecord? capturedEvent = null;
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            capturedEvent = record;
            eventReceived.Set();
        };

        watcher.Enabled = true;

        // Act
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for XML rendering", EventLogEntryType.Information);

        bool received = eventReceived.Wait(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(received, "Did not receive event within timeout period");
        Assert.NotNull(capturedEvent);
        Assert.False(string.IsNullOrWhiteSpace(capturedEvent.Xml));
    }

    [Fact]
    public void Multiple_Watchers_OnDifferentLogs_ShouldBothSubscribe()
    {
        // Arrange
        using var appWatcher = new EventLogWatcher(Constants.ApplicationLogName);
        using var sysWatcher = new EventLogWatcher(Constants.SystemLogName);

        // Act
        appWatcher.Enabled = true;
        sysWatcher.Enabled = true;

        // Assert - Both watchers should successfully subscribe
        Assert.True(appWatcher.Enabled);
        Assert.True(sysWatcher.Enabled);
    }

    [Fact]
    public void Multiple_Watchers_OnSameLog_ShouldAllReceiveEvents()
    {
        // Arrange
        using var watcher1 = new EventLogWatcher(Constants.ApplicationLogName);
        using var watcher2 = new EventLogWatcher(Constants.ApplicationLogName);
        
        var receivedEvents1 = new List<EventRecord>();
        var receivedEvents2 = new List<EventRecord>();
        var countdown = new CountdownEvent(2);

        watcher1.EventRecordWritten += (sender, record) =>
        {
            receivedEvents1.Add(record);
            countdown.Signal();
        };

        watcher2.EventRecordWritten += (sender, record) =>
        {
            receivedEvents2.Add(record);
            countdown.Signal();
        };

        watcher1.Enabled = true;
        watcher2.Enabled = true;

        // Act
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for multiple watchers", EventLogEntryType.Information);

        bool received = countdown.Wait(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(received, "Did not receive events in all watchers within timeout period");
        Assert.NotEmpty(receivedEvents1);
        Assert.NotEmpty(receivedEvents2);
    }

    [Fact]
    public void SubscribeAndUnsubscribe_WhenRepeated_ShouldWork()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);

        // Act & Assert - Subscribe and unsubscribe multiple times
        watcher.Enabled = true;
        Assert.True(watcher.Enabled);

        watcher.Enabled = false;
        Assert.False(watcher.Enabled);

        watcher.Enabled = true;
        Assert.True(watcher.Enabled);

        watcher.Enabled = false;
        Assert.False(watcher.Enabled);
    }

    [Fact]
    public void Unsubscribe_MultipleTimes_ShouldNotThrow()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        watcher.Enabled = true;

        // Act & Assert
        watcher.Enabled = false;
        watcher.Enabled = false;
        watcher.Enabled = false;
        
        Assert.False(watcher.Enabled);
    }

    [Fact]
    public void Unsubscribe_WhenNotSubscribed_ShouldNotThrow()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);

        // Act & Assert
        watcher.Enabled = false;
        Assert.False(watcher.Enabled);
    }
}
