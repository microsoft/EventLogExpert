// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Logging.Abstractions;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;

namespace EventLogExpert.Runtime.EventLog;

internal sealed class LogWatcherService : ILogWatcherService
{
    private readonly Dictionary<string, string?> _bookmarks = [];
    private readonly ITraceLogger _debugLogger;
    private readonly IDispatcher _dispatcher;
    private readonly List<string> _logsToWatch = [];
    private readonly Dictionary<EventLogId, Task> _pendingDisposals = [];
    private readonly Dictionary<string, bool> _renderXmlByLog = [];
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly Dictionary<string, EventLogId> _watchedLogIds = [];
    private readonly Dictionary<string, EventLogWatcher> _watchers = [];
    private readonly Lock _watchersLock = new();

    public LogWatcherService(
        IStateSelection<EventLogState, bool> newEventBufferIsFull,
        [FromKeyedServices(LogCategories.EventLog)] ITraceLogger debugLogger,
        IDispatcher dispatcher,
        IServiceScopeFactory serviceScopeFactory)
    {
        _debugLogger = debugLogger;
        _dispatcher = dispatcher;
        _serviceScopeFactory = serviceScopeFactory;

        newEventBufferIsFull.Select(s => s.NewEventBufferIsFull);

        newEventBufferIsFull.SelectedValueChanged += (sender, isFull) =>
        {
            if (isFull)
            {
                // Fire-and-forget stop; observe the aggregate so a disposal fault is logged, not an unobserved
                // ThreadPool exception (observing the constituents does not observe the WhenAll aggregate).
                ObserveFault(StopAllWatchersAsync());
            }
            else
            {
                StartWatching();
            }
        };
    }

    public void AddLog(string logName, EventLogId logId, string? bookmark, bool renderXml = false)
    {
        using (_watchersLock.EnterScope())
        {
            // Capture before DetachWatcher, which transiently empties _watchers during a same-name replace.
            bool wasEmpty = _logsToWatch.Count == 0;
            bool wasWatching = IsWatching();

            if (_watchedLogIds.TryGetValue(logName, out var existingId))
            {
                if (existingId == logId) { return; }

                // Register the displaced watcher's disposal under the detach's lock so a concurrent close of the old
                // id never sees a gap with neither the watcher nor its disposal.
                if (DetachWatcher(logName) is { } staleWatcher) { RegisterPendingDisposal(existingId, logName, staleWatcher); }
            }

            _watchedLogIds[logName] = logId;

            if (!_logsToWatch.Contains(logName)) { _logsToWatch.Add(logName); }

            _bookmarks[logName] = bookmark;
            _renderXmlByLog[logName] = renderXml;

            if (wasEmpty || wasWatching)
            {
                StartWatching(logName);
            }
        }
    }

    public Task RemoveAllAsync()
    {
        using (_watchersLock.EnterScope())
        {
            // Register every live watcher's disposal so close-all (and any concurrent single close) awaits it.
            // DetachWatcher clears _watchedLogIds, so capture the id first; snapshot the names before iterating.
            foreach (var logName in _logsToWatch.ToArray())
            {
                var id = _watchedLogIds.TryGetValue(logName, out var watchedId) ? watchedId : default;

                if (DetachWatcher(logName) is { } watcher) { RegisterPendingDisposal(id, logName, watcher); }
            }

            // Await every in-flight disposal (just-registered plus any prior replacement/buffer-full stop); the
            // settlement continuation removes each on success.
            return Task.WhenAll(_pendingDisposals.Values);
        }
    }

    public Task RemoveLogAsync(string logName, EventLogId logId)
    {
        using (_watchersLock.EnterScope())
        {
            // Register the live watcher's disposal (when this id is still the watched one) so a concurrent close-all
            // sees it too; a stale close for a prior id just awaits any disposal already tracked for it.
            if (_watchedLogIds.TryGetValue(logName, out var watchedId) && watchedId == logId &&
                DetachWatcher(logName) is { } watcher)
            {
                RegisterPendingDisposal(logId, logName, watcher);
            }

            // Await whatever disposal is in flight for this id without claiming it; the settlement continuation
            // removes it on success or retains a fault for a later close to observe.
            return _pendingDisposals.TryGetValue(logId, out var pending) ? pending : Task.CompletedTask;
        }
    }

    // Call under _watchersLock; the caller disposes the returned watcher OFF the lock (deadlock rule).
    private EventLogWatcher? DetachWatcher(string logName)
    {
        _logsToWatch.Remove(logName);
        _bookmarks.Remove(logName);
        _renderXmlByLog.Remove(logName);
        _watchedLogIds.Remove(logName);
        _watchers.Remove(logName, out var watcher);

        return watcher;
    }

    // Off-thread: EventLogWatcher.Unsubscribe blocks on in-flight callbacks that take _watchersLock, so disposing
    // under the lock would deadlock. Faults propagate: an awaiting close (or the pending-disposal observer) must
    // see a teardown failure rather than a false success.
    private Task DisposeWatcherAsync(string logName, EventLogWatcher watcher) =>
        Task.Run(() =>
        {
            watcher.Dispose();

            _debugLogger.Debug($"{nameof(LogWatcherService)} disposed the watcher for log {logName}.");
        });

    private bool IsWatching()
    {
        using var scope = _watchersLock.EnterScope();

        return _watchers.Keys.Count > 0;
    }

    private void ObserveFault(Task task) =>
        task.ContinueWith(
            faulted => _debugLogger.Warning(
                $"{nameof(LogWatcherService)} watcher teardown faulted: {faulted.Exception?.GetBaseException().Message}"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    // Call under _watchersLock; the blocking Dispose runs off-lock (deadlock rule). Tracks the disposal by id so a
    // later close awaits it, chaining onto any in-flight disposal for the same id (a buffer-full stop reuses the id).
    // Success self-removes; a fault is RETAINED (a later close sees it, not a false CompletedTask) and logged.
    private Task RegisterPendingDisposal(EventLogId id, string logName, EventLogWatcher watcher)
    {
        var disposal = DisposeWatcherAsync(logName, watcher);
        var tracked = _pendingDisposals.TryGetValue(id, out var existing) ? Task.WhenAll(existing, disposal) : disposal;
        _pendingDisposals[id] = tracked;

        tracked.ContinueWith(
            completed =>
            {
                using (_watchersLock.EnterScope())
                {
                    if (completed.IsCompletedSuccessfully &&
                        _pendingDisposals.TryGetValue(id, out var current) &&
                        ReferenceEquals(current, completed))
                    {
                        _pendingDisposals.Remove(id);
                    }
                }

                if (completed.IsFaulted)
                {
                    _debugLogger.Warning(
                        $"{nameof(LogWatcherService)} failed to dispose a retired watcher for log {logName}: {completed.Exception?.GetBaseException().Message}");
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return tracked;
    }

    private void StartWatching()
    {
        using var scope = _watchersLock.EnterScope();

        foreach (var logName in _logsToWatch)
        {
            StartWatching(logName);
        }
    }

    private void StartWatching(string logName)
    {
        using var scope = _watchersLock.EnterScope();

        if (_watchers.ContainsKey(logName)) { return; }

        // The id the watcher is created for; stamped on every dispatched event so a stale watcher's events are
        // dropped by the reopened log (they carry the old id) instead of being misrouted to it.
        var watcherLogId = _watchedLogIds.TryGetValue(logName, out var currentId) ? currentId : default;

        bool renderXml = _renderXmlByLog.TryGetValue(logName, out var flag) && flag;

        EventLogWatcher watcher = _bookmarks[logName] != null ?
            new EventLogWatcher(logName, _bookmarks[logName], renderXml) :
            new EventLogWatcher(logName, renderXml);

        _watchers.Add(logName, watcher);

        watcher.EventRecordWritten += (sender, eventArgs) =>
        {
            if (!eventArgs.IsSuccess) { return; }

            using var serviceScope = _serviceScopeFactory.CreateScope();
            var eventResolver = serviceScope.ServiceProvider.GetService<IEventResolver>();

            _debugLogger.Trace($"EventRecordWritten callback was called.");

            if (eventResolver is null)
            {
                _debugLogger.Warning($"{nameof(LogWatcherService)} event resolver is null in EventRecordWritten callback.");

                return;
            }

            using var scope = _watchersLock.EnterScope();

            // Drop events from a watcher being disposed asynchronously: it may still be mid-loop with the old
            // renderXml and would pollute the reopened log.
            if (!_watchers.TryGetValue(logName, out var activeWatcher) ||
                !ReferenceEquals(activeWatcher, watcher))
            {
                return;
            }

            _dispatcher.Dispatch(new AddEventAction(eventResolver.ResolveEvent(eventArgs), watcherLogId));
        };

        // Enabling reads every event since the last bookmark; do it off the UI thread.
        Task.Run(() =>
        {
            watcher.Enabled = true;

            _debugLogger.Debug($"{nameof(LogWatcherService)} started watching {logName}.");
        });
    }

    private Task StopAllWatchersAsync()
    {
        List<string> logNames;

        using (_watchersLock.EnterScope())
        {
            // Snapshot: StopWatchingAsync mutates _watchers below.
            logNames = [.. _watchers.Keys];
        }

        var tasks = new List<Task>(logNames.Count);

        foreach (var logName in logNames)
        {
            tasks.Add(StopWatchingAsync(logName));
        }

        return Task.WhenAll(tasks);
    }

    private Task StopWatchingAsync(string logName)
    {
        using var scope = _watchersLock.EnterScope();

        if (!_watchers.Remove(logName, out var watcher)) { return Task.CompletedTask; }

        // The log stays watched (it can resume when the buffer clears), so track the disposal under its id: a close
        // before it completes must still await the teardown. Reuses the id, so RegisterPendingDisposal chains.
        var id = _watchedLogIds.TryGetValue(logName, out var watchedId) ? watchedId : default;

        return RegisterPendingDisposal(id, logName, watcher);
    }
}
