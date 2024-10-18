// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Store.Settings;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.Eventing.Reader;

namespace EventLogExpert.UI.Store.EventLog;

public interface ILogWatcherService
{
    void AddLog(string logName, EventBookmark? bookmark);

    void RemoveAll();

    void RemoveLog(string logName);
}

public sealed class LiveLogWatcherService : ILogWatcherService
{
    private readonly Dictionary<string, EventBookmark?> _bookmarks = [];
    private readonly ITraceLogger _debugLogger;
    private readonly IDispatcher _dispatcher;
    private readonly List<string> _logsToWatch = [];
    private readonly IEventResolverCache _resolverCache;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IState<SettingsState> _settingsState;
    private readonly Dictionary<string, EventLogWatcher> _watchers = [];

    public LiveLogWatcherService(
        IStateSelection<EventLogState, bool> bufferFullStateSelection,
        ITraceLogger debugLogger,
        IDispatcher dispatcher,
        IEventResolverCache resolverCache,
        IServiceScopeFactory serviceScopeFactory,
        IState<SettingsState> settingsState)
    {
        _debugLogger = debugLogger;
        _dispatcher = dispatcher;
        _resolverCache = resolverCache;
        _serviceScopeFactory = serviceScopeFactory;
        _settingsState = settingsState;

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

    public void AddLog(string logName, EventBookmark? bookmark)
    {
        lock (this)
        {
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
    }

    public bool IsWatching()
    {
        lock (this)
        {
            return _watchers.Keys.Count > 0;
        }
    }

    public void RemoveAll()
    {
        lock (this)
        {
            while (_logsToWatch.Count > 0)
            {
                RemoveLog(_logsToWatch[0]);
            }
        }
    }

    public void RemoveLog(string logName)
    {
        lock (this)
        {
            _logsToWatch.Remove(logName);
            _bookmarks.Remove(logName);
            StopWatching(logName);
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

    private void StartWatching(string logName)
    {
        lock (this)
        {
            if (_watchers.ContainsKey(logName)) { return; }

            var query = new EventLogQuery(logName, PathType.LogName);

            EventLogWatcher watcher = _bookmarks[logName] != null ?
                new EventLogWatcher(query, _bookmarks[logName]) :
                new EventLogWatcher(query);

            _watchers.Add(logName, watcher);

            watcher.EventRecordWritten += (sender, eventArgs) =>
            {
                lock (this)
                {
                    using var serviceScope = _serviceScopeFactory.CreateScope();
                    var eventResolver = serviceScope.ServiceProvider.GetService<IEventResolver>();

                    _debugLogger.Trace("EventRecordWritten callback was called.");
                    _bookmarks[logName] = eventArgs.EventRecord.Bookmark;

                    if (eventResolver is null)
                    {
                        _debugLogger.Trace(
                            $"{nameof(LiveLogWatcherService)} _resolver is null in EventRecordWritten callback.");

                        return;
                    }

                    eventResolver.ResolveProviderDetails(eventArgs.EventRecord, logName);

                    _dispatcher.Dispatch(
                        new EventLogAction.AddEvent(
                            new DisplayEventModel(logName)
                            {
                                ActivityId = eventArgs.EventRecord.ActivityId,
                                ComputerName = _resolverCache.GetValue(eventArgs.EventRecord.MachineName),
                                Description = _resolverCache.GetDescription(
                                    eventResolver.ResolveDescription(eventArgs.EventRecord)),
                                Id = eventArgs.EventRecord.Id,
                                KeywordsDisplayNames = eventResolver.GetKeywordsFromBitmask(eventArgs.EventRecord)
                                    .Select(_resolverCache.GetValue).ToList(),
                                Level = Severity.GetString(eventArgs.EventRecord.Level),
                                LogName = _resolverCache.GetValue(eventArgs.EventRecord.LogName),
                                ProcessId = eventArgs.EventRecord.ProcessId,
                                RecordId = eventArgs.EventRecord.RecordId,
                                Source = _resolverCache.GetValue(eventArgs.EventRecord.ProviderName),
                                TaskCategory = _resolverCache.GetValue(
                                    eventResolver.ResolveTaskName(eventArgs.EventRecord)),
                                ThreadId = eventArgs.EventRecord.ThreadId,
                                TimeCreated = eventArgs.EventRecord.TimeCreated!.Value.ToUniversalTime(),
                                UserId = eventArgs.EventRecord.UserId,
                                Xml = _settingsState.Value.Config.IsXmlEnabled ? eventArgs.EventRecord.ToXml() : null
                            }));
                }
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
    }

    private void StopWatching(string logName)
    {
        lock (this)
        {
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

            _debugLogger.Trace($"{nameof(LiveLogWatcherService)} dispatched a task to stop the watcher for log {logName}.");
        }
    }
}
