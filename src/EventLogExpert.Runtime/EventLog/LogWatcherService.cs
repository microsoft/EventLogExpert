// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Logging;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Resolvers;
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
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly Dictionary<string, EventLogWatcher> _watchers = [];
    private readonly Lock _watchersLock = new();

    public LogWatcherService(
        IStateSelection<EventLogState, bool> newEventBufferIsFull,
        ITraceLogger debugLogger,
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
                // Buffer-full is a state change, not a coordinated delete: fire-and-forget
                // is fine because nothing downstream is waiting for the old per-event
                // resolver scopes to finish disposing.
                _ = StopAllWatchersAsync();
            }
            else
            {
                StartWatching();
            }
        };
    }

    public void AddLog(string logName, string? bookmark, bool renderXml = false)
    {
        using var scope = _watchersLock.EnterScope();

        if (_logsToWatch.Contains(logName))
        {
            throw new InvalidOperationException(
                $"Attempted to add log {logName} which is already present in {nameof(LogWatcherService)}.");
        }

        _logsToWatch.Add(logName);
        _bookmarks.Add(logName, bookmark);
        _renderXmlByLog[logName] = renderXml;

        // If this is the first log added, or if we're already watching
        // other logs, then we need to start watching this one.
        //
        // If we have _logsToWatch but no watchers, that means StopAllWatchersAsync()
        // was called due to a full buffer. In that case we do not want to
        // start watching the new log.
        if (_logsToWatch.Count == 1 || IsWatching())
        {
            StartWatching(logName);
        }
    }

    public Task RemoveAllAsync()
    {
        List<string> logNames;

        using (_watchersLock.EnterScope())
        {
            // Snapshot the names before iterating so RemoveLogAsync can mutate
            // _logsToWatch without invalidating our enumerator.
            logNames = [.. _logsToWatch];
        }

        var tasks = new List<Task>(logNames.Count);

        foreach (var logName in logNames)
        {
            tasks.Add(RemoveLogAsync(logName));
        }

        return Task.WhenAll(tasks);
    }

    public Task RemoveLogAsync(string logName)
    {
        EventLogWatcher? watcher;

        using (_watchersLock.EnterScope())
        {
            _logsToWatch.Remove(logName);
            _bookmarks.Remove(logName);
            _renderXmlByLog.Remove(logName);

            if (!_watchers.Remove(logName, out watcher)) { return Task.CompletedTask; }
        }

        // Always dispose on a background thread to avoid a deadlock if this is
        // called on the UI thread. EventLogWatcher.Unsubscribe blocks until all
        // in-flight ReadAndRaiseEvents callbacks have completed (so the per-event
        // service scopes — and the SQLite handles they hold — are released
        // before this Task completes).
        return Task.Run(() =>
        {
            watcher.Dispose();

            _debugLogger.Information($"{nameof(LogWatcherService)} disposed the old watcher for log {logName}.");
        });
    }

    private bool IsWatching()
    {
        using var scope = _watchersLock.EnterScope();

        return _watchers.Keys.Count > 0;
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

            // Guard against stale callbacks: a watcher being disposed asynchronously
            // can still be mid-loop processing buffered events with the old renderXml
            // setting. If the active watcher for this log is no longer this instance,
            // discard the event so it doesn't pollute the re-opened log with the
            // wrong XML state.
            if (!_watchers.TryGetValue(logName, out var activeWatcher) ||
                !ReferenceEquals(activeWatcher, watcher))
            {
                return;
            }

            _dispatcher.Dispatch(new AddEventAction(eventResolver.ResolveEvent(eventArgs)));
        };

        // When the watcher is enabled, it reads all the events since the
        // last bookmark. Do this on a background thread so we don't tie
        // up the UI.
        Task.Run(() =>
        {
            watcher.Enabled = true;

            _debugLogger.Information($"{nameof(LogWatcherService)} started watching {logName}.");
        });
    }

    private Task StopAllWatchersAsync()
    {
        List<string> logNames;

        using (_watchersLock.EnterScope())
        {
            // Snapshot keys before iterating; StopWatchingAsync mutates the dict.
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

        return Task.Run(() =>
        {
            watcher.Dispose();

            _debugLogger.Information($"{nameof(LogWatcherService)} disposed the old watcher for log {logName}.");
        });
    }
}
