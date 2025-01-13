// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Readers;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.UI.Store.EventLog;

public interface ILogWatcherService
{
    void AddLog(string logName, string? bookmark);

    void RemoveAll();

    void RemoveLog(string logName);
}

public sealed class LiveLogWatcherService : ILogWatcherService
{
    private readonly Dictionary<string, string?> _bookmarks = [];
    private readonly ITraceLogger _debugLogger;
    private readonly IDispatcher _dispatcher;
    private readonly List<string> _logsToWatch = [];
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly Dictionary<string, EventLogWatcher> _watchers = [];
    private readonly Lock _watchersLock = new();

    public LiveLogWatcherService(
        IStateSelection<EventLogState, bool> bufferFullStateSelection,
        ITraceLogger debugLogger,
        IDispatcher dispatcher,
        IServiceScopeFactory serviceScopeFactory)
    {
        _debugLogger = debugLogger;
        _dispatcher = dispatcher;
        _serviceScopeFactory = serviceScopeFactory;

        bufferFullStateSelection.Select(s => s.NewEventBufferIsFull);

        bufferFullStateSelection.SelectedValueChanged += (sender, isFull) =>
        {
            if (isFull)
            {
                StopWatching();
            }
            else
            {
                StartWatching();
            }
        };
    }

    public void AddLog(string logName, string? bookmark)
    {
        using var scope = _watchersLock.EnterScope();

        if (_logsToWatch.Contains(logName))
        {
            throw new InvalidOperationException(
                $"Attempted to add log {logName} which is already present in LiveLogWatcher.");
        }

        _logsToWatch.Add(logName);
        _bookmarks.Add(logName, bookmark);

        // If this is the first log added, or if we're already watching
        // other logs, then we need to start watching this one.
        //
        // If we have _logsToWatch but no watchers, that means StopWatching()
        // was called due to a full buffer. In that case we do not want to
        // start watching the new log.
        if (_logsToWatch.Count == 1 || IsWatching())
        {
            StartWatching(logName);
        }
    }

    public void RemoveAll()
    {
        using var scope = _watchersLock.EnterScope();

        while (_logsToWatch.Count > 0)
        {
            RemoveLog(_logsToWatch[0]);
        }
    }

    public void RemoveLog(string logName)
    {
        using var scope = _watchersLock.EnterScope();

        _logsToWatch.Remove(logName);
        _bookmarks.Remove(logName);
        StopWatching(logName);
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

        EventLogWatcher watcher = _bookmarks[logName] != null ?
            new EventLogWatcher(logName, _bookmarks[logName]) :
            new EventLogWatcher(logName);

        _watchers.Add(logName, watcher);

        watcher.EventRecordWritten += (sender, eventArgs) =>
        {
            if (!eventArgs.IsSuccess) { return; }

            using var serviceScope = _serviceScopeFactory.CreateScope();
            var eventResolver = serviceScope.ServiceProvider.GetService<IEventResolver>();

            _debugLogger.Trace("EventRecordWritten callback was called.");

            if (eventResolver is null)
            {
                _debugLogger.Trace(
                    $"{nameof(LiveLogWatcherService)} event resolver is null in EventRecordWritten callback.", LogLevel.Error);

                return;
            }

            using var scope = _watchersLock.EnterScope();

            _dispatcher.Dispatch(new EventLogAction.AddEvent(eventResolver.ResolveEvent(eventArgs)));
        };

        // When the watcher is enabled, it reads all the events since the
        // last bookmark. Do this on a background thread so we don't tie
        // up the UI.
        Task.Run(() =>
        {
            watcher.Enabled = true;

            _debugLogger.Trace($"{nameof(LiveLogWatcherService)} started watching {logName}.");
        });
    }

    private void StopWatching()
    {
        using var scope = _watchersLock.EnterScope();

        foreach (var logName in _watchers.Keys)
        {
            StopWatching(logName);
        }
    }

    private void StopWatching(string logName)
    {
        using var scope = _watchersLock.EnterScope();

        if (!_watchers.Remove(logName, out var watcher))
        {
            return;
        }

        // Always do this on a background thread to avoid a deadlock
        // if this is called on the UI thread. EventLogWatcher.Enabled = false
        // will cause the thread to block until all outstanding callbacks
        // have completed.
        Task.Run(() =>
        {
            watcher.Dispose();

            _debugLogger.Trace($"{nameof(LiveLogWatcherService)} disposed the old watcher for log {logName}.");
        });

        _debugLogger.Trace(
            $"{nameof(LiveLogWatcherService)} dispatched a task to stop the watcher for log {logName}.");
    }
}
