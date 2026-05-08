// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Tests.TestUtils.Constants;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

namespace EventLogExpert.Eventing.IntegrationTests.Readers;

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
    public void Constructor_WithInvalidLogName_ShouldThrowFileNotFoundException()
    {
        // Arrange
        var invalidLogName = "NonExistentLog_" + Guid.NewGuid();

        // Act & Assert
        Assert.Throws<FileNotFoundException>(() => new EventLogWatcher(invalidLogName));
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
        int eventCount = 0;
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            Interlocked.Increment(ref eventCount);
            eventReceived.Set();
        };

        watcher.Enabled = true;

        // Act — dispose, then write a stimulus event to prove the handler is
        // no longer wired up. EventLogWatcher.Dispose drains in-flight
        // callbacks before returning. Snapshot the count and reset the signal
        // *after* the drain so any ambient callbacks delivered during the
        // live subscription window (between Enabled=true and Dispose) cannot
        // bleed into the post-stimulus assertion.
        watcher.Dispose();
        int countBefore = Volatile.Read(ref eventCount);
        eventReceived.Reset();

        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event after dispose", EventLogEntryType.Information);

        bool received = eventReceived.Wait(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        // Assert
        int actual = Volatile.Read(ref eventCount);
        Assert.False(received, "Should not have received any event after dispose");
        Assert.Equal(countBefore, actual);
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
    public void Dispose_ShouldReleaseUnderlyingWaitHandle()
    {
        // Arrange
        var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        watcher.Enabled = true;

        // Reach into the private AutoResetEvent the watcher owns so we can
        // assert the kernel handle is released — the leak this test guards
        // against (per Copilot review on PR #516) was discovered precisely
        // because there was no externally-observable signal of disposal.
        var newEventsField = typeof(EventLogWatcher).GetField(
            "_newEvents",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(
            newEventsField is not null,
            "Private field _newEvents not found on EventLogWatcher — was it renamed? Update Dispose_ShouldReleaseUnderlyingWaitHandle.");

        var newEvents = (AutoResetEvent?)newEventsField.GetValue(watcher);
        Assert.NotNull(newEvents);

        // Act
        watcher.Dispose();

        // Assert
        Assert.True(
            newEvents.SafeWaitHandle.IsClosed,
            "AutoResetEvent.SafeWaitHandle.IsClosed should be true after Dispose; otherwise the OS event handle leaks across watcher lifetimes.");

        // Idempotency: the field-level Dispose unconditionally calls
        // _newEvents.Dispose(), so a second top-level Dispose() must be
        // gated by the _disposed fast-path to avoid a second call.
        watcher.Dispose();
        Assert.True(newEvents.SafeWaitHandle.IsClosed);
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
    public void Dispose_WhenCalledFromHandler_ShouldThrowInvalidOperationException()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        Exception? observed = null;
        var observedSet = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            // Capture the exception inside the handler — the SUT isolates
            // subscriber exceptions per its public contract, so an uncaught
            // throw here would be silently swallowed and the test would hang.
            try
            {
                ((EventLogWatcher)sender!).Dispose();
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

        // Act
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for reentrant Dispose", EventLogEntryType.Information);

        bool received = observedSet.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(received, "Handler was never invoked, so reentrancy was not exercised");
        var captured = Volatile.Read(ref observed);
        Assert.NotNull(captured);
        var ex = Assert.IsType<InvalidOperationException>(captured);
        Assert.Contains("cannot be stopped from within", ex.Message);

        // Cleanup happens via `using var watcher` from the test thread, which
        // is not the ThreadPool callback thread, so the reentrancy guard does
        // not trigger and Dispose completes normally.
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
    public async Task Enabled_WhenRacingFromMultipleThreads_ShouldNotHangOrCorruptState()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);

        // Act: race many Subscribe/Unsubscribe pairs to maximize the window
        // for Subscribe to mutate _subscriptionHandle/_waitHandle while
        // another thread is in Unsubscribe. Without the lifecycle lock, two
        // Enabled=false callers can both pass the _waitHandle null check and
        // both call RegisteredWaitHandle.Unregister; the second call returns
        // false (already unregistered) and does NOT signal its wait object,
        // so its WaitOne blocks indefinitely. Separately, an Enabled=true
        // racing with Enabled=false can leave _subscriptionHandle disposed
        // mid-Subscribe, making the next ProcessNewEvents invocation crash
        // on a closed native handle.
        var tasks = new List<Task>(capacity: 100);
        var ct = TestContext.Current.CancellationToken;

        for (int i = 0; i < 50; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                try { watcher.Enabled = true; }
                catch (InvalidOperationException) { /* loser of a Subscribe race — expected */ }
            }, ct));
            tasks.Add(Task.Run(() => watcher.Enabled = false, ct));
        }

        var allDone = Task.WhenAll(tasks);

        // Assert: 30s budget far exceeds any legitimate Subscribe/Unsubscribe
        // drain across 100 toggles. A TimeoutException here means the
        // RegisteredWaitHandle race regression has returned. Any other
        // exception (e.g., ObjectDisposedException from a torn-down handle)
        // surfaces via the await.
        await allDone.WaitAsync(TimeSpan.FromSeconds(30), ct);
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
    public void Enabled_WhenSetToFalseFromHandler_ShouldThrowInvalidOperationException()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        Exception? observed = null;
        var observedSet = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            // Same reentrancy contract as Dispose: Enabled = false also calls
            // Unsubscribe, which blocks waiting for in-flight callbacks. Calling
            // it from a callback would self-deadlock, so the SUT throws.
            try
            {
                ((EventLogWatcher)sender!).Enabled = false;
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

        // Act
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for reentrant Enabled=false", EventLogEntryType.Information);

        bool received = observedSet.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(received, "Handler was never invoked, so reentrancy was not exercised");
        var captured = Volatile.Read(ref observed);
        Assert.NotNull(captured);
        var ex = Assert.IsType<InvalidOperationException>(captured);
        Assert.Contains("cannot be stopped from within", ex.Message);
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
    public void Enabled_WhenSetToTrueAfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        watcher.Dispose();

        // Act & Assert: post-dispose Subscribe is gated by an explicit
        // _disposed check inside the lifecycle lock so callers see a clean
        // ObjectDisposedException instead of a confusing native-handle
        // failure (e.g., from _newEvents.SafeWaitHandle access after
        // _newEvents has itself been disposed).
        Assert.Throws<ObjectDisposedException>(() => watcher.Enabled = true);
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
        int eventCount = 0;
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            Interlocked.Increment(ref eventCount);
            eventReceived.Set();
        };

        // Act — toggle the watcher off then on. Safe to reset the counter
        // and signal between Disable and the second Enable: Unsubscribe
        // blocks until in-flight callbacks complete, so no handler can fire
        // between the count clear and the Reset below.
        watcher.Enabled = true;
        watcher.Enabled = false;
        Interlocked.Exchange(ref eventCount, 0);
        eventReceived.Reset();

        watcher.Enabled = true;

        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event after resubscribe", EventLogEntryType.Information);

        bool received = eventReceived.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        int actual = Volatile.Read(ref eventCount);
        Assert.True(received, "Did not receive event within timeout period");
        Assert.True(actual > 0, $"Expected at least one event after resubscribe, but got {actual}.");
    }

    [Fact]
    public void EventRecordWritten_AfterUnsubscribe_ShouldStopReceivingEvents()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        int eventCount = 0;
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            Interlocked.Increment(ref eventCount);
            eventReceived.Set();
        };

        watcher.Enabled = true;

        // Act — disable the watcher, then snapshot the post-action count and
        // clear the signal accumulated during the populate phase. Safe:
        // EventLogWatcher.Unsubscribe blocks until in-flight callbacks
        // complete, so no handler can fire between the count capture and the
        // Reset below.
        watcher.Enabled = false;
        int countBefore = Volatile.Read(ref eventCount);
        eventReceived.Reset();

        // The stimulus that proves Disable worked: write an event AFTER
        // Disable and verify the handler does not fire. Without the stimulus
        // the test would vacuously pass because no event would arrive in the
        // wait window regardless of whether Disable did anything.
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event after unsubscribe", EventLogEntryType.Information);

        bool fired = eventReceived.Wait(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        // Assert
        int actual = Volatile.Read(ref eventCount);
        Assert.False(fired, "Should not have received any event after unsubscribe");
        Assert.Equal(countBefore, actual);
    }

    [Fact]
    public void EventRecordWritten_ForNormalEvent_ShouldHaveNullError()
    {
        // Arrange
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

        // Act — Write a normal event. EventRecord.Error is populated by
        // ProcessNewEvents only when EvtRender throws, which is not reachable
        // through the public API for a well-formed log entry. This test pins
        // the happy-path invariant (Error is null on a normal event); see
        // WithInvalidBookmark_ShouldThrowAndNotMaskAsUnauthorizedAccessException
        // for an exercise of the failure path.
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for normal-path null Error invariant", EventLogEntryType.Information);

        bool received = eventReceived.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        var snapshot = receivedEvents.ToArray();
        Assert.True(received, "Did not receive event within timeout period");
        Assert.NotEmpty(snapshot);
        // Assert only the first signaled record (the stimulus). Late ambient
        // callbacks queued before ToArray could otherwise broaden the check
        // beyond what the test stimulus controls.
        Assert.Null(snapshot[0].Error);
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
            // Volatile.Write provides cross-thread visibility for the
            // reference assignment; the test thread reads via Volatile.Read
            // after the signal Wait. Last-event-wins semantics: a later
            // ambient callback may overwrite this value before the test
            // reads it, which is acceptable here because the assertion is
            // an "any watcher event has this invariant" check.
            Volatile.Write(ref capturedEvent, record);
            eventReceived.Set();
        };

        watcher.Enabled = true;

        // Act
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for record ID", EventLogEntryType.Information);

        bool received = eventReceived.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        var snapshot = Volatile.Read(ref capturedEvent);
        Assert.True(received, "Did not receive event within timeout period");
        Assert.NotNull(snapshot);
        Assert.NotNull(snapshot.RecordId);
        Assert.True(snapshot.RecordId > 0);
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
            Volatile.Write(ref capturedEvent, record);
            eventReceived.Set();
        };

        watcher.Enabled = true;

        // Act
        var beforeWrite = DateTime.UtcNow;

        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for timestamp", EventLogEntryType.Information);

        bool received = eventReceived.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var afterWrite = DateTime.UtcNow.AddSeconds(1); // Add buffer for processing

        // Assert
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
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        EventRecord? capturedEvent = null;
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            Volatile.Write(ref capturedEvent, record);
            eventReceived.Set();
        };

        watcher.Enabled = true;

        // Act
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for path validation", EventLogEntryType.Information);

        bool received = eventReceived.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        var snapshot = Volatile.Read(ref capturedEvent);
        Assert.True(received, "Did not receive event within timeout period");
        Assert.NotNull(snapshot);
        Assert.Equal(Constants.ApplicationLogName, snapshot.PathName);
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
            Volatile.Write(ref capturedEvent, record);
            eventReceived.Set();
        };

        watcher.Enabled = true;

        // Act
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for properties", EventLogEntryType.Information);

        bool received = eventReceived.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        var snapshot = Volatile.Read(ref capturedEvent);
        Assert.True(received, "Did not receive event within timeout period");
        Assert.NotNull(snapshot);
        Assert.NotNull(snapshot.Properties);
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
            Volatile.Write(ref capturedSender, sender);
            eventReceived.Set();
        };

        watcher.Enabled = true;

        // Act
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for sender validation", EventLogEntryType.Information);

        bool received = eventReceived.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        var snapshot = Volatile.Read(ref capturedSender);
        Assert.True(received, "Did not receive event within timeout period");
        Assert.NotNull(snapshot);
        Assert.Same(watcher, snapshot);
    }

    [Fact]
    public async Task EventRecordWritten_ShouldReceiveEventsInOrder()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        const int expectedCount = 3;
        var receivedEvents = new ConcurrentQueue<EventRecord>();
        int signalCount = 0;
        var countdown = new CountdownEvent(expectedCount);

        watcher.EventRecordWritten += (sender, record) =>
        {
            receivedEvents.Enqueue(record);

            // Guard CountdownEvent.Signal: ambient callbacks past the
            // expected count would throw InvalidOperationException on the
            // callback thread.
            int count = Interlocked.Increment(ref signalCount);

            if (count <= expectedCount)
            {
                countdown.Signal();
            }
        };

        watcher.Enabled = true;

        // Act
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;

        eventLog.WriteEntry("Event A", EventLogEntryType.Information);
        await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        eventLog.WriteEntry("Event B", EventLogEntryType.Information);
        await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        eventLog.WriteEntry("Event C", EventLogEntryType.Information);

        bool received = countdown.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        // Assert
        // Snapshot the queue: ConcurrentQueue.ToArray returns a moment-in-
        // time copy in enqueue order. Late ambient callbacks may continue
        // to enqueue after this point, but they will not affect the snapshot.
        var snapshot = receivedEvents.ToArray();
        Assert.True(received, "Did not receive all events within timeout period");
        Assert.True(snapshot.Length >= expectedCount, $"Expected at least {expectedCount} events in snapshot, but got {snapshot.Length}.");

        // Verify events are in chronological order (by TimeCreated)
        for (int i = 1; i < Math.Min(expectedCount, snapshot.Length); i++)
        {
            Assert.True(snapshot[i].TimeCreated >= snapshot[i - 1].TimeCreated,
                $"Event {i} was received before event {i - 1}");
        }
    }

    [Fact]
    public void EventRecordWritten_WhenHandlerThrows_ShouldStillDeliverFutureEvents()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        int attempts = 0;
        var firstObserved = new ManualResetEventSlim(false);
        var laterObserved = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            // Signal "first invocation observed" BEFORE throwing so the
            // stimulus thread can release the second write only after the
            // throw has happened. Per-subscriber isolation in the SUT
            // (introduced alongside this test) is what lets later invocations
            // fire at all — without it the throw would short-circuit the
            // multicast invoke and bubble into the ThreadPool worker.
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

        // Act — write the stimulus, wait for the first (throwing) invocation,
        // then write a second event to prove the watcher still delivers.
        eventLog.WriteEntry("Test event 1 (handler throws on first invocation)", EventLogEntryType.Information);
        bool firstReceived = firstObserved.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(firstReceived, "First handler invocation was not observed");

        eventLog.WriteEntry("Test event 2 (handler should still fire)", EventLogEntryType.Information);
        bool laterReceived = laterObserved.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        int actual = Volatile.Read(ref attempts);
        Assert.True(laterReceived,
            $"Handler did not fire again after throwing on first invocation; attempts={actual}");
        Assert.True(actual >= 2,
            $"Expected at least 2 handler invocations after recovery, got {actual}");
    }

    [Fact]
    public void EventRecordWritten_WhenMultipleEventsWritten_ShouldReceiveAll()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        const int expectedCount = 3;
        int eventCount = 0;
        var countdown = new CountdownEvent(expectedCount);

        watcher.EventRecordWritten += (sender, record) =>
        {
            // Guard CountdownEvent.Signal: the shared Application log can
            // produce ambient callbacks that, if signaled past zero, throw
            // InvalidOperationException on the callback thread.
            int count = Interlocked.Increment(ref eventCount);

            if (count <= expectedCount)
            {
                countdown.Signal();
            }
        };

        watcher.Enabled = true;

        // Act
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;

        eventLog.WriteEntry("Test event 1", EventLogEntryType.Information);
        eventLog.WriteEntry("Test event 2", EventLogEntryType.Warning);
        eventLog.WriteEntry("Test event 3", EventLogEntryType.Error);

        bool received = countdown.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        // Assert
        int actual = Volatile.Read(ref eventCount);
        Assert.True(received, $"Did not receive all events within timeout period. Got {actual}.");
        Assert.True(actual >= expectedCount, $"Expected at least {expectedCount} events, but got {actual}.");
    }

    [Fact]
    public void EventRecordWritten_WhenNotSubscribed_ShouldNotReceiveEvents()
    {
        // Arrange — handler is attached via += but watcher.Enabled is never
        // set to true, so no native subscription should be active.
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        int eventCount = 0;
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            Interlocked.Increment(ref eventCount);
            eventReceived.Set();
        };

        // Act — write an event to prove the negative case. Without this
        // stimulus the test would vacuously pass because no event would
        // arrive in the wait window regardless of whether the watcher was
        // actually subscribed.
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event with watcher not enabled", EventLogEntryType.Information);

        bool received = eventReceived.Wait(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        // Assert
        int actual = Volatile.Read(ref eventCount);
        Assert.False(received, "Should not have received any event when watcher is not Enabled");
        Assert.Equal(0, actual);
    }

    [Fact]
    public void EventRecordWritten_WhenOneOfMultipleHandlersThrows_ShouldStillNotifyOthers()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        int failingCount = 0;
        int succeedingCount = 0;
        var succeedingObserved = new ManualResetEventSlim(false);

        // First subscriber always throws — simulates a poorly written consumer.
        // Per-subscriber isolation must prevent it from blocking the second.
        watcher.EventRecordWritten += (sender, record) =>
        {
            Interlocked.Increment(ref failingCount);
            throw new InvalidOperationException("subscriber A always fails");
        };

        // Second subscriber must still be invoked despite the first throwing.
        watcher.EventRecordWritten += (sender, record) =>
        {
            Interlocked.Increment(ref succeedingCount);
            succeedingObserved.Set();
        };

        watcher.Enabled = true;

        // Act
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for multi-subscriber isolation", EventLogEntryType.Information);

        bool received = succeedingObserved.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
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
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        int eventCount = 0;
        EventRecord? capturedEvent = null;
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            // Capture the latest record for the per-record non-null
            // invariant. Volatile.Write pairs with Volatile.Read in the
            // assertion to guarantee cross-thread visibility.
            Volatile.Write(ref capturedEvent, record);
            Interlocked.Increment(ref eventCount);
            eventReceived.Set();
        };

        // Act
        watcher.Enabled = true;

        // Write a test event to Application log
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for EventLogWatcher", EventLogEntryType.Information);

        bool received = eventReceived.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        int actual = Volatile.Read(ref eventCount);
        var snapshot = Volatile.Read(ref capturedEvent);
        Assert.True(received, "Did not receive event within timeout period");
        Assert.True(actual > 0, $"Expected at least one event, but got {actual}.");
        Assert.NotNull(snapshot);
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
        int eventCount = 0;
        var eventReceived = new ManualResetEventSlim(false);

        watcher.EventRecordWritten += (sender, record) =>
        {
            Interlocked.Increment(ref eventCount);
            eventReceived.Set();
        };

        // Act
        watcher.Enabled = true;

        // Write a new event
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event after bookmark", EventLogEntryType.Information);

        bool received = eventReceived.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        int actual = Volatile.Read(ref eventCount);
        Assert.True(received, "Did not receive event within timeout period");
        Assert.True(actual > 0, $"Expected at least one event after bookmark, but got {actual}.");
    }

    [Fact]
    public async Task EventRecordWritten_WithConcurrentEventWrites_ShouldHandleAllEvents()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        const int expectedCount = 5;
        int eventCount = 0;
        var countdown = new CountdownEvent(expectedCount);

        watcher.EventRecordWritten += (sender, record) =>
        {
            // Guard CountdownEvent.Signal: ambient callbacks past the
            // expected count would throw InvalidOperationException on the
            // callback thread.
            int count = Interlocked.Increment(ref eventCount);

            if (count <= expectedCount)
            {
                countdown.Signal();
            }
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
        bool received = countdown.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        // Assert
        int actual = Volatile.Read(ref eventCount);
        Assert.True(received, $"Did not receive all events within timeout period. Got {actual} events.");
        Assert.True(actual >= expectedCount, $"Expected at least {expectedCount} events, but got {actual}.");
    }

    [Fact]
    public void EventRecordWritten_WithInvalidBookmark_ShouldThrowAndNotMaskAsUnauthorizedAccessException()
    {
        // Arrange
        var invalidBookmark = "InvalidBookmarkString";
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName, invalidBookmark, renderXml: false);

        // Act
        var ex = Record.Exception(() => watcher.Enabled = true);

        // Assert
        // The bookmark XML failure flows through ThrowEventLogException and the
        // exact Win32 mapping may shift across Windows versions. Capture the
        // stable invariants instead of pinning the type: bad bookmarks must
        // surface an exception and must not be masked as UAE.
        Assert.NotNull(ex);
        Assert.IsNotType<UnauthorizedAccessException>(ex);
    }

    [Fact]
    public void EventRecordWritten_WithMultipleSubscribers_ShouldNotifyAll()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);
        const int expectedCountPerSubscriber = 1;
        int eventCount1 = 0;
        int eventCount2 = 0;
        var countdown = new CountdownEvent(2);

        watcher.EventRecordWritten += (sender, record) =>
        {
            // Guard CountdownEvent.Signal against ambient over-signal.
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

        // Act
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for multiple subscribers", EventLogEntryType.Information);

        bool received = countdown.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        int actual1 = Volatile.Read(ref eventCount1);
        int actual2 = Volatile.Read(ref eventCount2);
        Assert.True(received, "Did not receive events in all subscribers within timeout period");
        Assert.True(actual1 > 0, $"Subscriber 1 expected at least one event, but got {actual1}.");
        Assert.True(actual2 > 0, $"Subscriber 2 expected at least one event, but got {actual2}.");
    }

    [Fact]
    public async Task EventRecordWritten_WithNoSubscribers_ShouldNotThrow()
    {
        // Arrange
        using var watcher = new EventLogWatcher(Constants.ApplicationLogName);

        // Act
        watcher.Enabled = true;

        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event with no subscribers", EventLogEntryType.Information);

        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

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
            Volatile.Write(ref capturedEvent, record);
            eventReceived.Set();
        };

        watcher.Enabled = true;

        // Act
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event without XML", EventLogEntryType.Information);

        bool received = eventReceived.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        var snapshot = Volatile.Read(ref capturedEvent);
        Assert.True(received, "Did not receive event within timeout period");
        Assert.NotNull(snapshot);
        Assert.Null(snapshot.Xml);
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
            Volatile.Write(ref capturedEvent, record);
            eventReceived.Set();
        };

        watcher.Enabled = true;

        // Act
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for XML rendering", EventLogEntryType.Information);

        bool received = eventReceived.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        var snapshot = Volatile.Read(ref capturedEvent);
        Assert.True(received, "Did not receive event within timeout period");
        Assert.NotNull(snapshot);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Xml));
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

        const int expectedCountPerWatcher = 1;
        int eventCount1 = 0;
        int eventCount2 = 0;
        var countdown = new CountdownEvent(2);

        watcher1.EventRecordWritten += (sender, record) =>
        {
            // Guard CountdownEvent.Signal against ambient over-signal.
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

        // Act
        using var eventLog = new EventLog(Constants.ApplicationLogName);
        eventLog.Source = Constants.ApplicationLogName;
        eventLog.WriteEntry("Test event for multiple watchers", EventLogEntryType.Information);

        bool received = countdown.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        int actual1 = Volatile.Read(ref eventCount1);
        int actual2 = Volatile.Read(ref eventCount2);
        Assert.True(received, "Did not receive events in all watchers within timeout period");
        Assert.True(actual1 > 0, $"Watcher 1 expected at least one event, but got {actual1}.");
        Assert.True(actual2 > 0, $"Watcher 2 expected at least one event, but got {actual2}.");
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
