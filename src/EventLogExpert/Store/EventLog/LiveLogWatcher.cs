// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.EventResolvers;
using EventLogExpert.Library.Helpers;
using Microsoft.Extensions.FileSystemGlobbing;
using System;
using System.Diagnostics.Eventing.Reader;

namespace EventLogExpert.Store.EventLog;

/// <summary>
///   This class is a wrapper around EventLogWatcher so we can maintain
///   state as we create and destroy multiple EventLogWatchers without dumping
///   all that state at the top level of EventLogState. Most importantly:
///   - Keep track of the last bookmark so we can stop watching and start watching again later
///     with a new EventLogWatcher.
///   - Hold on to the resolver and the debug logger so we can create callbacks
///     for new EventLogWatchers.
/// </summary>
public class LiveLogWatcher
{
    private readonly string _logName;
    private readonly ITraceLogger _debugLogger;
    private readonly IEventResolver _resolver;
    private readonly Fluxor.IDispatcher _dispatcher;
    private EventBookmark? _bookmark;
    private EventLogWatcher? _watcher;

    public LiveLogWatcher(string LogName, EventBookmark? Bookmark, ITraceLogger DebugLogger, IEventResolver Resolver, Fluxor.IDispatcher Dispatcher)
    {
        _logName = LogName;
        _bookmark = Bookmark;
        _debugLogger = DebugLogger;
        _resolver = Resolver;
        _dispatcher = Dispatcher;
    }

    public bool IsWatching => _watcher != null;

    public void StartWatching()
    {
        if (_watcher != null) return;

        var query = new EventLogQuery(_logName, PathType.LogName);

        if (_bookmark != null)
        {
            _watcher = new EventLogWatcher(query, _bookmark);
        }
        else
        {
            _watcher = new EventLogWatcher(query);
        }

        _watcher.EventRecordWritten += (watcher, eventArgs) =>
        {
            lock (this)
            {
                _debugLogger.Trace("EventRecordWritten callback was called.");
                _bookmark = eventArgs.EventRecord.Bookmark;
                var resolved = _resolver.Resolve(eventArgs.EventRecord);
                _dispatcher.Dispatch(new EventLogAction.AddEvent(resolved));
            }
        };

        _watcher.Enabled = true;

        _debugLogger.Trace("LiveLogWatcher started watching.");
    }

    public void StopWatching()
    {
        if (_watcher == null) return;

        // Always do this on a background thread to avoid a deadlock
        // if this is called on the UI thread. EventLogWatcher.Enabled = false
        // will cause the thread to block until all outstanding callbacks
        // have completed.
        var oldWatcher = _watcher;
        _watcher = null;
        Task.Run(() =>
            {
                oldWatcher.Enabled = false;
                oldWatcher.Dispose();
                _debugLogger.Trace("LiveLogWatcher disposed the old watcher.");
            });

        _debugLogger.Trace("LiveLogWatcher dispatched a task to stop the watcher.");
    }
}
