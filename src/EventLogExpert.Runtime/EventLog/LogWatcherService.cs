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
    private readonly Dictionary<string, bool> _renderXmlByLog = [];
    private readonly Dictionary<EventLogId, Task> _retiringWatchers = [];
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
                // Fire-and-forget: nothing downstream waits for the old per-event resolver scopes to finish disposing.
                _ = StopAllWatchersAsync();
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
            if (_watchedLogIds.TryGetValue(logName, out var existingId))
            {
                if (existingId == logId) { return; }

                // Retire under the detach's lock so a concurrent close of the old id never sees a gap with neither
                // the watcher nor its disposal.
                if (DetachWatcher(logName) is { } staleWatcher) { RetireWatcher(existingId, logName, staleWatcher); }
            }

            _watchedLogIds[logName] = logId;

            if (!_logsToWatch.Contains(logName)) { _logsToWatch.Add(logName); }

            _bookmarks[logName] = bookmark;
            _renderXmlByLog[logName] = renderXml;

            if (_logsToWatch.Count == 1 || IsWatching())
            {
                StartWatching(logName);
            }
        }
    }

    public Task RemoveAllAsync()
    {
        List<(string Name, EventLogWatcher Watcher)> detached = [];
        List<Task> retiring;

        using (_watchersLock.EnterScope())
        {
            // Snapshot: DetachWatcher mutates _logsToWatch below.
            foreach (var logName in _logsToWatch.ToArray())
            {
                if (DetachWatcher(logName) is { } watcher) { detached.Add((logName, watcher)); }
            }

            // Also await any watcher already being disposed by a same-name replacement.
            retiring = [.. _retiringWatchers.Values];
        }

        List<Task> disposals = [];

        foreach (var (name, watcher) in detached) { disposals.Add(DisposeWatcherAsync(name, watcher)); }

        disposals.AddRange(retiring);

        return Task.WhenAll(disposals);
    }

    public Task RemoveLogAsync(string logName, EventLogId logId)
    {
        EventLogWatcher? watcher;

        using (_watchersLock.EnterScope())
        {
            // A stale close for a prior open must not drop a same-name log reopened under a new id.
            if (!_watchedLogIds.TryGetValue(logName, out var watchedId) || watchedId != logId)
            {
                // If this id's watcher was displaced by a reopen, await its disposal so close still releases its handles.
                return _retiringWatchers.TryGetValue(logId, out var retiring) ? retiring : Task.CompletedTask;
            }

            watcher = DetachWatcher(logName);
        }

        return watcher is null ? Task.CompletedTask : DisposeWatcherAsync(logName, watcher);
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
    // under the lock would deadlock.
    private Task DisposeWatcherAsync(string logName, EventLogWatcher watcher) =>
        Task.Run(() =>
        {
            try
            {
                watcher.Dispose();

                _debugLogger.Debug($"{nameof(LogWatcherService)} disposed the watcher for log {logName}.");
            }
            catch (Exception ex)
            {
                // Never fault: a fire-and-forget caller would surface an unobserved throw as an unhandled ThreadPool exception.
                _debugLogger.Warning($"{nameof(LogWatcherService)} failed to dispose the watcher for log {logName}: {ex.Message}");
            }
        });

    private bool IsWatching()
    {
        using var scope = _watchersLock.EnterScope();

        return _watchers.Keys.Count > 0;
    }

    // Call under _watchersLock. Record the disposal BEFORE scheduling it so a concurrent close of the retired id can
    // await teardown without leaking the entry.
    private void RetireWatcher(EventLogId retiredId, string logName, EventLogWatcher watcher)
    {
        var retirement = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _retiringWatchers[retiredId] = retirement.Task;

        _ = CompleteWhenDisposedAsync();

        async Task CompleteWhenDisposedAsync()
        {
            try
            {
                await DisposeWatcherAsync(logName, watcher);
            }
            finally
            {
                using (_watchersLock.EnterScope()) { _retiringWatchers.Remove(retiredId); }

                retirement.TrySetResult();
            }
        }
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

            _dispatcher.Dispatch(new AddEventAction(eventResolver.ResolveEvent(eventArgs)));
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
        EventLogWatcher? watcher;

        using (_watchersLock.EnterScope())
        {
            if (!_watchers.Remove(logName, out watcher)) { return Task.CompletedTask; }
        }

        return DisposeWatcherAsync(logName, watcher);
    }
}
