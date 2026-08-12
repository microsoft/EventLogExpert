// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.TestUtils.Constants;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

namespace EventLogExpert.Eventing.IntegrationTests.Readers;

public sealed class EventLogWatcherTests
{
    private static readonly TimeSpan s_concurrentTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan s_interEventDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan s_longTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan s_negativeWait = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan s_settleDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan s_testTimeout = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(Constants.ApplicationLogName)]
    [InlineData(Constants.SystemLogName)]
    public void Constructor_WithCommonLogs_ShouldCreateWatcher(string logName)
    {
        using var watcher = new EventLogWatcher(logName);

        Assert.NotNull(watcher);
        Assert.False(watcher.Enabled);
    }

    [Fact]
    public void Constructor_WithEmptyLogName_ShouldThrowArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new EventLogWatcher(string.Empty));
    }

    [Fact]
    public void Constructor_WithInvalidLogName_ShouldThrowFileNotFoundException()
    {
        var invalidLogName = "NonExistentLog_" + Guid.NewGuid();

        Assert.Throws<FileNotFoundException>(() => new EventLogWatcher(invalidLogName));
    }

    [Fact]
    public void Constructor_WithNullLogName_ShouldThrowArgumentException()
    {
        Assert.Throws<ArgumentNullException>(() => new EventLogWatcher(null!));
    }

    [Fact]
    public void Constructor_WithValidBookmark_ShouldCreateWatcher()
    {
        using var reader = new EventLogReader(Constants.ApplicationLogName, LogPathType.Channel);

        reader.TryGetEvents(out _, 1);

        var bookmark = reader.LastBookmark;

        using var watcher = new EventLogWatcher(Constants.ApplicationLogName, bookmark);

        Assert.NotNull(watcher);
    }

    [Fact]
    public void Constructor_WithWhitespaceLogName_ShouldThrowArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new EventLogWatcher("   "));
    }

    [Fact]
    public void Dispose_AfterDispose_ShouldNotReceiveEvents()
    {
        var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        int eventCount = 0;
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            Interlocked.Increment(ref eventCount);
            eventReceived.Set();
        };

        watcher.Enabled = true;

        watcher.Dispose();
        int countBefore = Volatile.Read(ref eventCount);
        eventReceived.Reset();

        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event after dispose", EventLogEntryType.Information);

        bool received = eventReceived.Wait(s_negativeWait, TestContext.Current.CancellationToken);

        int actual = Volatile.Read(ref eventCount);
        Assert.False(received, "Should not have received any event after dispose");
        Assert.Equal(countBefore, actual);
    }

    [Fact]
    public void Dispose_BeforeSubscribe_ShouldNotThrow()
    {
        var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        Assert.False(watcher.Enabled);

        watcher.Dispose();
    }

    [Fact]
    public void Dispose_ShouldReleaseUnderlyingWaitHandle()
    {
        var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        watcher.Enabled = true;

        var newEventsField = typeof(EventLogWatcher).GetField(
            "_newEvents",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(newEventsField is not null, "_newEvents field missing — was it renamed?");

        var newEvents = (AutoResetEvent?)newEventsField.GetValue(watcher);
        Assert.NotNull(newEvents);

        watcher.Dispose();

        Assert.True(newEvents.SafeWaitHandle.IsClosed, "AutoResetEvent kernel handle leaked after Dispose.");

        watcher.Dispose();
        Assert.True(newEvents.SafeWaitHandle.IsClosed);
    }

    [Fact]
    public void Dispose_WhenCalledFromHandler_ShouldThrowInvalidOperationException()
    {
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        Exception? observed = null;
        var observedSet = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            try
            {
                Assert.NotNull(sender);
                ((EventLogWatcher)sender).Dispose();
            }
            catch (Exception ex)
            {
                Volatile.Write(ref observed, ex);
            }
            finally
            {
                observedSet.Set();
            }
        };

        watcher.Enabled = true;

        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for reentrant Dispose", EventLogEntryType.Information);

        bool received = observedSet.Wait(s_testTimeout, TestContext.Current.CancellationToken);

        Assert.True(received, "Handler was never invoked, so reentrancy was not exercised");
        var captured = Volatile.Read(ref observed);
        Assert.NotNull(captured);
        var ex = Assert.IsType<InvalidOperationException>(captured);
        Assert.Contains("cannot be stopped from within", ex.Message);
    }

    [Fact]
    public void Dispose_WhenCalledMultipleTimes_ShouldNotThrow()
    {
        var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        watcher.Enabled = true;

        watcher.Dispose();
        watcher.Dispose();
        watcher.Dispose();
    }

    [Fact]
    public void Dispose_WhenCalled_ShouldUnsubscribe()
    {
        var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        watcher.Enabled = true;

        watcher.Dispose();

        Assert.False(watcher.Enabled);
    }

    [Fact]
    public void Dispose_WhenNotSubscribed_ShouldNotThrow()
    {
        var watcher = new EventLogWatcher(Constants.ApplicationLogName);

        watcher.Dispose();
    }

    [Fact]
    public void Dispose_WhileSubscribed_ShouldUnsubscribeAndDispose()
    {
        var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        watcher.Enabled = true;
        Assert.True(watcher.Enabled);

        watcher.Dispose();

        Assert.False(watcher.Enabled);
    }

    [Fact]
    public void Enabled_AfterMultipleToggle_ShouldMaintainCorrectState()
    {
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);

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
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);

        Assert.False(watcher.Enabled);
    }

    [Fact]
    public async Task Enabled_WhenRacingFromMultipleThreads_ShouldNotHangOrCorruptState()
    {
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);

        var tasks = new List<Task>(100);
        var ct = TestContext.Current.CancellationToken;

        for (int i = 0; i < 50; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                try { watcher.Enabled = true; }
                catch (InvalidOperationException) { }
            }, ct));
            tasks.Add(Task.Run(() => watcher.Enabled = false, ct));
        }

        var allDone = Task.WhenAll(tasks);

        await allDone.WaitAsync(s_concurrentTimeout, ct);
    }

    [Fact]
    public void Enabled_WhenSetToFalseFromHandler_ShouldThrowInvalidOperationException()
    {
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        Exception? observed = null;
        var observedSet = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            try
            {
                Assert.NotNull(sender);
                ((EventLogWatcher)sender).Enabled = false;
            }
            catch (Exception ex)
            {
                Volatile.Write(ref observed, ex);
            }
            finally
            {
                observedSet.Set();
            }
        };

        watcher.Enabled = true;

        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for reentrant Enabled=false", EventLogEntryType.Information);

        bool received = observedSet.Wait(s_testTimeout, TestContext.Current.CancellationToken);

        Assert.True(received, "Handler was never invoked, so reentrancy was not exercised");
        var captured = Volatile.Read(ref observed);
        Assert.NotNull(captured);
        var ex = Assert.IsType<InvalidOperationException>(captured);
        Assert.Contains("cannot be stopped from within", ex.Message);
    }

    [Fact]
    public void Enabled_WhenSetToFalseTwice_ShouldNotThrow()
    {
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        watcher.Enabled = true;
        watcher.Enabled = false;

        watcher.Enabled = false;
        Assert.False(watcher.Enabled);
    }

    [Fact]
    public void Enabled_WhenSetToFalse_ShouldUnsubscribe()
    {
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        watcher.Enabled = true;

        watcher.Enabled = false;

        Assert.False(watcher.Enabled);
    }

    [Fact]
    public void Enabled_WhenSetToSameValue_ShouldNotThrow()
    {
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        watcher.Enabled = false;

        watcher.Enabled = false;
        Assert.False(watcher.Enabled);
    }

    [Fact]
    public void Enabled_WhenSetToTrueAfterDispose_ShouldThrowObjectDisposedException()
    {
        var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        watcher.Dispose();

        Assert.Throws<ObjectDisposedException>(() => watcher.Enabled = true);
    }

    [Fact]
    public void Enabled_WhenSetToTrueTwice_ShouldNotThrow()
    {
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        watcher.Enabled = true;

        watcher.Enabled = true;

        Assert.True(watcher.Enabled);
    }

    [Fact]
    public void Enabled_WhenSetToTrueTwice_ShouldRemainEnabled()
    {
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        watcher.Enabled = true;

        watcher.Enabled = true;

        Assert.True(watcher.Enabled);
    }

    [Fact]
    public void Enabled_WhenSetToTrue_ShouldSubscribe()
    {
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);

        watcher.Enabled = true;

        Assert.True(watcher.Enabled);
    }

    [Fact]
    public void Enabled_WhenToggled_ShouldUpdateState()
    {
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);

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
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        int eventCount = 0;
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            Interlocked.Increment(ref eventCount);
            eventReceived.Set();
        };

        watcher.Enabled = true;
        watcher.Enabled = false;
        Interlocked.Exchange(ref eventCount, 0);
        eventReceived.Reset();

        watcher.Enabled = true;

        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event after resubscribe", EventLogEntryType.Information);

        bool received = eventReceived.Wait(s_testTimeout, TestContext.Current.CancellationToken);

        int actual = Volatile.Read(ref eventCount);
        Assert.True(received, "Did not receive event within timeout period");
        Assert.True(actual > 0, $"Expected at least one event after resubscribe, but got {actual}.");
    }

    [Fact]
    public void EventRecordWritten_AfterUnsubscribe_ShouldStopReceivingEvents()
    {
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        int eventCount = 0;
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            Interlocked.Increment(ref eventCount);
            eventReceived.Set();
        };

        watcher.Enabled = true;

        watcher.Enabled = false;
        int countBefore = Volatile.Read(ref eventCount);
        eventReceived.Reset();

        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event after unsubscribe", EventLogEntryType.Information);

        bool fired = eventReceived.Wait(s_negativeWait, TestContext.Current.CancellationToken);

        int actual = Volatile.Read(ref eventCount);
        Assert.False(fired, "Should not have received any event after unsubscribe");
        Assert.Equal(countBefore, actual);
    }

    [Fact]
    public void EventRecordWritten_ForNormalEvent_ShouldHaveNullError()
    {
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        var receivedEvents = new ConcurrentQueue<EventRecord>();
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            receivedEvents.Enqueue(record);

            if (!eventReceived.IsSet)
            {
                eventReceived.Set();
            }
        };

        watcher.Enabled = true;

        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for normal-path null Error invariant", EventLogEntryType.Information);

        bool received = eventReceived.Wait(s_testTimeout, TestContext.Current.CancellationToken);

        var snapshot = receivedEvents.ToArray();
        Assert.True(received, "Did not receive event within timeout period");
        Assert.NotEmpty(snapshot);
        Assert.Null(snapshot[0].Error);
    }

    [Fact]
    public void EventRecordWritten_ShouldHaveRecordId()
    {
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        EventRecord? capturedEvent = null;
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            Volatile.Write(ref capturedEvent, record);
            eventReceived.Set();
        };

        watcher.Enabled = true;

        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for record ID", EventLogEntryType.Information);

        bool received = eventReceived.Wait(s_testTimeout, TestContext.Current.CancellationToken);

        var snapshot = Volatile.Read(ref capturedEvent);
        Assert.True(received, "Did not receive event within timeout period");
        Assert.NotNull(snapshot);
        Assert.NotNull(snapshot.RecordId);
        Assert.True(snapshot.RecordId > 0);
    }

    [Fact]
    public void EventRecordWritten_ShouldHaveValidTimeCreated()
    {
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        EventRecord? capturedEvent = null;
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            Volatile.Write(ref capturedEvent, record);
            eventReceived.Set();
        };

        watcher.Enabled = true;

        var beforeWrite = DateTime.UtcNow;

        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for timestamp", EventLogEntryType.Information);

        bool received = eventReceived.Wait(s_testTimeout, TestContext.Current.CancellationToken);
        var afterWrite = DateTime.UtcNow.AddSeconds(1);

        var snapshot = Volatile.Read(ref capturedEvent);
        Assert.True(received, "Did not receive event within timeout period");
        Assert.NotNull(snapshot);
        Assert.NotEqual(default(DateTime), snapshot.TimeCreated);
        Assert.True(snapshot.TimeCreated >= beforeWrite.AddSeconds(-1));
        Assert.True(snapshot.TimeCreated <= afterWrite);
    }

    [Fact]
    public void EventRecordWritten_ShouldIncludePathName()
    {
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        EventRecord? capturedEvent = null;
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            Volatile.Write(ref capturedEvent, record);
            eventReceived.Set();
        };

        watcher.Enabled = true;

        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for path validation", EventLogEntryType.Information);

        bool received = eventReceived.Wait(s_testTimeout, TestContext.Current.CancellationToken);

        var snapshot = Volatile.Read(ref capturedEvent);
        Assert.True(received, "Did not receive event within timeout period");
        Assert.NotNull(snapshot);
        Assert.Equal(Constants.ApplicationLogName, snapshot.PathName);
    }

    [Fact]
    public void EventRecordWritten_ShouldIncludeProperties()
    {
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        EventRecord? capturedEvent = null;
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            Volatile.Write(ref capturedEvent, record);
            eventReceived.Set();
        };

        watcher.Enabled = true;

        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for properties", EventLogEntryType.Information);

        bool received = eventReceived.Wait(s_testTimeout, TestContext.Current.CancellationToken);

        var snapshot = Volatile.Read(ref capturedEvent);
        Assert.True(received, "Did not receive event within timeout period");
        Assert.NotNull(snapshot);
        Assert.False(snapshot.Properties.IsDefault);
    }

    [Fact]
    public void EventRecordWritten_ShouldProvideCorrectSender()
    {
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        object? capturedSender = null;
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            Volatile.Write(ref capturedSender, sender);
            eventReceived.Set();
        };

        watcher.Enabled = true;

        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for sender validation", EventLogEntryType.Information);

        bool received = eventReceived.Wait(s_testTimeout, TestContext.Current.CancellationToken);

        var snapshot = Volatile.Read(ref capturedSender);
        Assert.True(received, "Did not receive event within timeout period");
        Assert.NotNull(snapshot);
        Assert.Same(watcher, snapshot);
    }

    [Fact]
    public async Task EventRecordWritten_ShouldReceiveEventsInOrder()
    {
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        const int expectedCount = 3;
        var receivedEvents = new ConcurrentQueue<EventRecord>();
        int signalCount = 0;
        var countdown = new CountdownEvent(expectedCount);

        watcher.EventRecordWritten += (sender, record) =>
        {
            receivedEvents.Enqueue(record);

            int count = Interlocked.Increment(ref signalCount);

            if (count <= expectedCount)
            {
                countdown.Signal();
            }
        };

        watcher.Enabled = true;

        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;

        eventLog.WriteEntry("Event A", EventLogEntryType.Information);
        await Task.Delay(s_interEventDelay, TestContext.Current.CancellationToken);
        eventLog.WriteEntry("Event B", EventLogEntryType.Information);
        await Task.Delay(s_interEventDelay, TestContext.Current.CancellationToken);
        eventLog.WriteEntry("Event C", EventLogEntryType.Information);

        bool received = countdown.Wait(s_longTimeout, TestContext.Current.CancellationToken);

        var snapshot = receivedEvents.ToArray();
        Assert.True(received, "Did not receive all events within timeout period");
        Assert.True(snapshot.Length >= expectedCount, $"Expected at least {expectedCount} events in snapshot, but got {snapshot.Length}.");

        for (int i = 1; i < Math.Min(expectedCount, snapshot.Length); i++)
        {
            Assert.True(snapshot[i].TimeCreated >= snapshot[i - 1].TimeCreated,
                $"Event {i} was received before event {i - 1}");
        }
    }

    [Fact]
    public void EventRecordWritten_WhenHandlerThrows_ShouldStillDeliverFutureEvents()
    {
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        int attempts = 0;
        var firstObserved = new ManualResetEventSlim(false);
        var laterObserved = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            int n = Interlocked.Increment(ref attempts);

            if (n == 1)
            {
                firstObserved.Set();
                throw new InvalidOperationException("simulated handler failure");
            }

            laterObserved.Set();
        };

        watcher.Enabled = true;

        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;

        eventLog.WriteEntry("Test event 1 (handler throws on first invocation)", EventLogEntryType.Information);
        bool firstReceived = firstObserved.Wait(s_testTimeout, TestContext.Current.CancellationToken);
        Assert.True(firstReceived, "First handler invocation was not observed");

        eventLog.WriteEntry("Test event 2 (handler should still fire)", EventLogEntryType.Information);
        bool laterReceived = laterObserved.Wait(s_testTimeout, TestContext.Current.CancellationToken);

        int actual = Volatile.Read(ref attempts);
        Assert.True(laterReceived,
            $"Handler did not fire again after throwing on first invocation; attempts={actual}");
        Assert.True(actual >= 2,
            $"Expected at least 2 handler invocations after recovery, got {actual}");
    }

    [Fact]
    public void EventRecordWritten_WhenMultipleEventsWritten_ShouldReceiveAll()
    {
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        const int expectedCount = 3;
        int eventCount = 0;
        var countdown = new CountdownEvent(expectedCount);

        watcher.EventRecordWritten += (sender, record) =>
        {
            int count = Interlocked.Increment(ref eventCount);

            if (count <= expectedCount)
            {
                countdown.Signal();
            }
        };

        watcher.Enabled = true;

        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;

        eventLog.WriteEntry("Test event 1", EventLogEntryType.Information);
        eventLog.WriteEntry("Test event 2", EventLogEntryType.Warning);
        eventLog.WriteEntry("Test event 3", EventLogEntryType.Error);

        bool received = countdown.Wait(s_longTimeout, TestContext.Current.CancellationToken);

        int actual = Volatile.Read(ref eventCount);
        Assert.True(received, $"Did not receive all events within timeout period. Got {actual}.");
        Assert.True(actual >= expectedCount, $"Expected at least {expectedCount} events, but got {actual}.");
    }

    [Fact]
    public void EventRecordWritten_WhenNotSubscribed_ShouldNotReceiveEvents()
    {
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        int eventCount = 0;
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            Interlocked.Increment(ref eventCount);
            eventReceived.Set();
        };

        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event with watcher not enabled", EventLogEntryType.Information);

        bool received = eventReceived.Wait(s_negativeWait, TestContext.Current.CancellationToken);

        int actual = Volatile.Read(ref eventCount);
        Assert.False(received, "Should not have received any event when watcher is not Enabled");
        Assert.Equal(0, actual);
    }

    [Fact]
    public void EventRecordWritten_WhenOneOfMultipleHandlersThrows_ShouldStillNotifyOthers()
    {
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        int failingCount = 0;
        int succeedingCount = 0;
        var succeedingObserved = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            Interlocked.Increment(ref failingCount);
            throw new InvalidOperationException("subscriber A always fails");
        };

        watcher.EventRecordWritten += (sender, record) =>
        {
            Interlocked.Increment(ref succeedingCount);
            succeedingObserved.Set();
        };

        watcher.Enabled = true;

        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for multi-subscriber isolation", EventLogEntryType.Information);

        bool received = succeedingObserved.Wait(s_testTimeout, TestContext.Current.CancellationToken);

        int failed = Volatile.Read(ref failingCount);
        int succeeded = Volatile.Read(ref succeedingCount);
        Assert.True(received,
            $"Second subscriber was never invoked despite first subscriber throwing; failed={failed}, succeeded={succeeded}");
        Assert.True(failed >= 1, $"Expected first (throwing) subscriber to be invoked at least once, got {failed}");
        Assert.True(succeeded >= 1, $"Expected second (succeeding) subscriber to be invoked at least once, got {succeeded}");
    }

    [Fact]
    public void EventRecordWritten_WhenSubscribed_ShouldReceiveEvents()
    {
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        int eventCount = 0;
        EventRecord? capturedEvent = null;
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            Volatile.Write(ref capturedEvent, record);
            Interlocked.Increment(ref eventCount);
            eventReceived.Set();
        };

        watcher.Enabled = true;

        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for EventLogWatcher", EventLogEntryType.Information);

        bool received = eventReceived.Wait(s_testTimeout, TestContext.Current.CancellationToken);

        int actual = Volatile.Read(ref eventCount);
        var snapshot = Volatile.Read(ref capturedEvent);
        Assert.True(received, "Did not receive event within timeout period");
        Assert.True(actual > 0, $"Expected at least one event, but got {actual}.");
        Assert.NotNull(snapshot);
    }

    [Fact]
    public void EventRecordWritten_WithBookmark_ShouldReceiveNewEvents()
    {
        using var reader = new EventLogReader(Constants.ApplicationLogName, LogPathType.Channel);
        reader.TryGetEvents(out _, 1);
        var bookmark = reader.LastBookmark;

        using var watcher = new EventLogWatcher(Constants.ApplicationLogName, bookmark);
        int eventCount = 0;
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            Interlocked.Increment(ref eventCount);
            eventReceived.Set();
        };

        watcher.Enabled = true;

        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event after bookmark", EventLogEntryType.Information);

        bool received = eventReceived.Wait(s_testTimeout, TestContext.Current.CancellationToken);

        int actual = Volatile.Read(ref eventCount);
        Assert.True(received, "Did not receive event within timeout period");
        Assert.True(actual > 0, $"Expected at least one event after bookmark, but got {actual}.");
    }

    [Fact]
    public async Task EventRecordWritten_WithConcurrentEventWrites_ShouldHandleAllEvents()
    {
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        const int expectedCount = 5;
        int eventCount = 0;
        var countdown = new CountdownEvent(expectedCount);

        watcher.EventRecordWritten += (sender, record) =>
        {
            int count = Interlocked.Increment(ref eventCount);

            if (count <= expectedCount)
            {
                countdown.Signal();
            }
        };

        watcher.Enabled = true;

        var tasks = new[]
        {
            Task.Run(() =>
            {
                using var log = new EventLog(Constants.ApplicationLogName);
                log.Source = Constants.ApplicationLogName;
                log.WriteEntry("Concurrent event 1", EventLogEntryType.Information);
            }, TestContext.Current.CancellationToken),
            Task.Run(() =>
            {
                using var log = new EventLog(Constants.ApplicationLogName);
                log.Source = Constants.ApplicationLogName;
                log.WriteEntry("Concurrent event 2", EventLogEntryType.Information);
            }, TestContext.Current.CancellationToken),
            Task.Run(() =>
            {
                using var log = new EventLog(Constants.ApplicationLogName);
                log.Source = Constants.ApplicationLogName;
                log.WriteEntry("Concurrent event 3", EventLogEntryType.Information);
            }, TestContext.Current.CancellationToken),
            Task.Run(() =>
            {
                using var log = new EventLog(Constants.ApplicationLogName);
                log.Source = Constants.ApplicationLogName;
                log.WriteEntry("Concurrent event 4", EventLogEntryType.Information);
            }, TestContext.Current.CancellationToken),
            Task.Run(() =>
            {
                using var log = new EventLog(Constants.ApplicationLogName);
                log.Source = Constants.ApplicationLogName;
                log.WriteEntry("Concurrent event 5", EventLogEntryType.Information);
            }, TestContext.Current.CancellationToken)
        };

        await Task.WhenAll(tasks);
        bool received = countdown.Wait(s_longTimeout, TestContext.Current.CancellationToken);

        int actual = Volatile.Read(ref eventCount);
        Assert.True(received, $"Did not receive all events within timeout period. Got {actual} events.");
        Assert.True(actual >= expectedCount, $"Expected at least {expectedCount} events, but got {actual}.");
    }

    [Fact]
    public void EventRecordWritten_WithInvalidBookmark_ShouldThrowAndNotMaskAsUnauthorizedAccessException()
    {
        var invalidBookmark = "InvalidBookmarkString";
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName, invalidBookmark);

        var ex = Record.Exception(() => watcher.Enabled = true);

        Assert.NotNull(ex);
        Assert.IsNotType<UnauthorizedAccessException>(ex);
    }

    [Fact]
    public void EventRecordWritten_WithMultipleSubscribers_ShouldNotifyAll()
    {
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        const int expectedCountPerSubscriber = 1;
        int eventCount1 = 0;
        int eventCount2 = 0;
        var countdown = new CountdownEvent(2);

        watcher.EventRecordWritten += (sender, record) =>
        {
            int count = Interlocked.Increment(ref eventCount1);

            if (count <= expectedCountPerSubscriber)
            {
                countdown.Signal();
            }
        };

        watcher.EventRecordWritten += (sender, record) =>
        {
            int count = Interlocked.Increment(ref eventCount2);

            if (count <= expectedCountPerSubscriber)
            {
                countdown.Signal();
            }
        };

        watcher.Enabled = true;

        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for multiple subscribers", EventLogEntryType.Information);

        bool received = countdown.Wait(s_testTimeout, TestContext.Current.CancellationToken);

        int actual1 = Volatile.Read(ref eventCount1);
        int actual2 = Volatile.Read(ref eventCount2);
        Assert.True(received, "Did not receive events in all subscribers within timeout period");
        Assert.True(actual1 > 0, $"Subscriber 1 expected at least one event, but got {actual1}.");
        Assert.True(actual2 > 0, $"Subscriber 2 expected at least one event, but got {actual2}.");
    }

    [Fact]
    public async Task EventRecordWritten_WithNoSubscribers_ShouldNotThrow()
    {
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);

        watcher.Enabled = true;

        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event with no subscribers", EventLogEntryType.Information);

        await Task.Delay(s_settleDelay, TestContext.Current.CancellationToken);

        Assert.True(watcher.Enabled);
    }

    [Fact]
    public void EventRecordWritten_WithRenderXmlFalse_ShouldNotIncludeXml()
    {
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        EventRecord? capturedEvent = null;
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            Volatile.Write(ref capturedEvent, record);
            eventReceived.Set();
        };

        watcher.Enabled = true;

        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event without XML", EventLogEntryType.Information);

        bool received = eventReceived.Wait(s_testTimeout, TestContext.Current.CancellationToken);

        var snapshot = Volatile.Read(ref capturedEvent);
        Assert.True(received, "Did not receive event within timeout period");
        Assert.NotNull(snapshot);
        Assert.Null(snapshot.Xml);
    }

    [Fact]
    public void EventRecordWritten_WithRenderXmlTrue_ShouldIncludeXml()
    {
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName, true);
        EventRecord? capturedEvent = null;
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            Volatile.Write(ref capturedEvent, record);
            eventReceived.Set();
        };

        watcher.Enabled = true;

        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for XML rendering", EventLogEntryType.Information);

        bool received = eventReceived.Wait(s_testTimeout, TestContext.Current.CancellationToken);

        var snapshot = Volatile.Read(ref capturedEvent);
        Assert.True(received, "Did not receive event within timeout period");
        Assert.NotNull(snapshot);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Xml));
    }

    [Fact]
    public void Multiple_Watchers_OnDifferentLogs_ShouldBothSubscribe()
    {
        using var appWatcher = new EventLogWatcher(Constants.ApplicationLogName);
        using var sysWatcher = new EventLogWatcher(Constants.SystemLogName);

        appWatcher.Enabled = true;
        sysWatcher.Enabled = true;

        Assert.True(appWatcher.Enabled);
        Assert.True(sysWatcher.Enabled);
    }

    [Fact]
    public void Multiple_Watchers_OnSameLog_ShouldAllReceiveEvents()
    {
        using var watcher1 = new EventLogWatcher(Constants.ApplicationLogName);
        using var watcher2 = new EventLogWatcher(Constants.ApplicationLogName);

        const int expectedCountPerWatcher = 1;
        int eventCount1 = 0;
        int eventCount2 = 0;
        var countdown = new CountdownEvent(2);

        watcher1.EventRecordWritten += (sender, record) =>
        {
            int count = Interlocked.Increment(ref eventCount1);

            if (count <= expectedCountPerWatcher)
            {
                countdown.Signal();
            }
        };

        watcher2.EventRecordWritten += (sender, record) =>
        {
            int count = Interlocked.Increment(ref eventCount2);

            if (count <= expectedCountPerWatcher)
            {
                countdown.Signal();
            }
        };

        watcher1.Enabled = true;
        watcher2.Enabled = true;

        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for multiple watchers", EventLogEntryType.Information);

        bool received = countdown.Wait(s_testTimeout, TestContext.Current.CancellationToken);

        int actual1 = Volatile.Read(ref eventCount1);
        int actual2 = Volatile.Read(ref eventCount2);
        Assert.True(received, "Did not receive events in all watchers within timeout period");
        Assert.True(actual1 > 0, $"Watcher 1 expected at least one event, but got {actual1}.");
        Assert.True(actual2 > 0, $"Watcher 2 expected at least one event, but got {actual2}.");
    }

    [Fact]
    public void SubscribeAndUnsubscribe_WhenRepeated_ShouldWork()
    {
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);

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
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        watcher.Enabled = true;

        watcher.Enabled = false;
        watcher.Enabled = false;
        watcher.Enabled = false;

        Assert.False(watcher.Enabled);
    }

    [Fact]
    public void Unsubscribe_WhenNotSubscribed_ShouldNotThrow()
    {
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);

        watcher.Enabled = false;
        Assert.False(watcher.Enabled);
    }
}
