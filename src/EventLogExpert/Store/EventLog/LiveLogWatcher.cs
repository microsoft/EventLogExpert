// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.EventResolvers;
using EventLogExpert.Library.Helpers;
using Fluxor;
using System.Diagnostics.Eventing.Reader;

namespace EventLogExpert.Store.EventLog;

public interface ILogWatcherService
{
    void AddLog(string LogName, EventBookmark? Bookmark);

    void RemoveLog(string LogName);

    void RemoveAll();
}

public class LiveLogWatcher : ILogWatcherService
{
    private readonly ITraceLogger _debugLogger;
    private readonly IEventResolver _resolver;
    private readonly Fluxor.IDispatcher _dispatcher;
    private List<string> _logsToWatch = new();
    private Dictionary<string, EventBookmark?> _bookmarks = new();
    private Dictionary<string, EventLogWatcher> _watchers = new();

    public LiveLogWatcher(ITraceLogger DebugLogger, IEventResolver Resolver, Fluxor.IDispatcher Dispatcher, IStateSelection<EventLogState, bool> bufferFullStateSelection)
    {
        _debugLogger = DebugLogger;
        _resolver = Resolver;
        _dispatcher = Dispatcher;
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

    public bool IsWatching()
    {
        lock (this)
        {
            return _watchers.Keys.Any();
        }
    }

    public void AddLog(string LogName, EventBookmark? Bookmark)
    {
        lock (this)
        {
            if (_logsToWatch.Contains(LogName))
            {
                throw new InvalidOperationException($"Attempted to add log {LogName} which is already present in LiveLogWatcher.");
            }

            _logsToWatch.Add(LogName);
            _bookmarks.Add(LogName, Bookmark);

            // If this is the first log added, or if we're already watching
            // other logs, then we need to start watching this one.
            //
            // If we have _logsToWatch but no watchers, that means StopWatching()
            // was called due to a full buffer. In that case we do not want to
            // start watching the new log.
            if (_logsToWatch.Count == 1 || IsWatching())
            {
                StartWatching(LogName);
            }
        }
    }

    public void RemoveAll()
    {
        lock (this)
        {
            while (_logsToWatch.Count > 0 )
            {
                RemoveLog(_logsToWatch[0]);
            }
        }
    }

    public void RemoveLog(string LogName)
    {
        lock (this)
        {
            _logsToWatch.Remove(LogName);
            _bookmarks.Remove(LogName);
            if (_watchers.ContainsKey(LogName))
            {
                var watcher = _watchers[LogName];
                _watchers.Remove(LogName);
                watcher.Dispose();
                _debugLogger.Trace($"Disposed watcher for log {LogName}.");
            }
        }
    }

    public void StartWatching()
    {
        lock (this)
        {
            foreach (var logName in _logsToWatch)
            {
                StartWatching(logName);
            }
        }
    }

    public void StopWatching()
    {
        lock (this)
        {
            foreach (var logName in _watchers.Keys)
            {
                StopWatching(logName);
            }
        }
    }

    private void StartWatching(string LogName)
    {
        lock (this)
        {
            if (_watchers.ContainsKey(LogName)) return;

            var query = new EventLogQuery(LogName, PathType.LogName);

            EventLogWatcher watcher;

            if (_bookmarks[LogName] != null)
            {
                watcher = new EventLogWatcher(query, _bookmarks[LogName]);
            }
            else
            {
                watcher = new EventLogWatcher(query);
            }

            _watchers.Add(LogName, watcher);

            watcher.EventRecordWritten += (watcher, eventArgs) =>
            {
                lock (this)
                {
                    _debugLogger.Trace("EventRecordWritten callback was called.");
                    _bookmarks[LogName] = eventArgs.EventRecord.Bookmark;
                    var resolved = _resolver.Resolve(eventArgs.EventRecord, LogName);
                    _dispatcher.Dispatch(new EventLogAction.AddEvent(resolved));
                }
            };

            // When the watcher is enabled, it reads all the events since the
            // last bookmark. Do this on a background thread so we don't tie
            // up the UI.
            Task.Run(() =>
            {
                watcher.Enabled = true;

                _debugLogger.Trace($"LiveLogWatcher started watching {LogName}.");
            });
        }
    }

    private void StopWatching(string LogName)
    {
        lock (this)
        {
            if (!_watchers.ContainsKey(LogName)) return;

            // Always do this on a background thread to avoid a deadlock
            // if this is called on the UI thread. EventLogWatcher.Enabled = false
            // will cause the thread to block until all outstanding callbacks
            // have completed.
            var oldWatcher = _watchers[LogName];
            _watchers.Remove(LogName);
            Task.Run(() =>
            {
                oldWatcher.Enabled = false;
                oldWatcher.Dispose();
                _debugLogger.Trace($"LiveLogWatcher disposed the old watcher for log {LogName}.");
            });

            _debugLogger.Trace($"LiveLogWatcher dispatched a task to stop the watcher for log {LogName}.");
        }
    }
}
