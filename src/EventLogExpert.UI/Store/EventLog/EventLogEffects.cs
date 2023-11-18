// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Store.StatusBar;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.UI.Store.EventLog;

public sealed class EventLogEffects(
    IServiceProvider serviceProvider,
    ITraceLogger debugLogger,
    ILogWatcherService logWatcherService)
{
    [EffectMethod]
    public async Task HandleOpenLogAction(EventLogAction.OpenLog action, IDispatcher dispatcher)
    {
        EventLogReader reader =
            action.LogType == EventLogState.LogType.Live ?
                new EventLogReader(action.LogName, PathType.LogName) :
                new EventLogReader(action.LogName, PathType.FilePath);

        // Do this on a background thread so we don't hang the UI
        await Task.Run(() =>
        {
            IEventResolver? eventResolver;

            try
            {
                eventResolver = serviceProvider.GetService<IEventResolver>();
            }
            catch (Exception ex)
            {
                dispatcher.Dispatch(new StatusBarAction.SetResolverStatus($"{ex.GetType}: {ex.Message}"));
                return;
            }

            if (eventResolver == null)
            {
                dispatcher.Dispatch(new StatusBarAction.SetResolverStatus($"Error: No event resolver available."));
                return;
            }

            try
            {
                var activityId = Guid.NewGuid();

                var sw = new Stopwatch();
                sw.Start();

                List<DisplayEventModel> events = [];
                HashSet<int> eventIdsAll = [];
                HashSet<Guid?> eventActivityIdsAll = [];
                HashSet<string> eventProviderNamesAll = [];
                HashSet<string> eventTaskNamesAll = [];
                HashSet<string> eventKeywordNamesAll = [];
                EventRecord lastEvent = null!;

                while (reader.ReadEvent() is { } e)
                {
                    lastEvent = e;
                    var resolved = eventResolver.Resolve(e, action.LogName);
                    eventIdsAll.Add(resolved.Id);
                    eventActivityIdsAll.Add(resolved.ActivityId);
                    eventProviderNamesAll.Add(resolved.Source);
                    eventTaskNamesAll.Add(resolved.TaskCategory);
                    eventKeywordNamesAll.UnionWith(resolved.KeywordsDisplayNames);

                    events.Add(resolved);

                    if (sw.ElapsedMilliseconds > 1000)
                    {
                        sw.Restart();
                        dispatcher.Dispatch(new EventLogAction.SetEventsLoading(activityId, events.Count));
                    }
                }

                dispatcher.Dispatch(new EventLogAction.LoadEvents(
                    action.LogName,
                    action.LogType,
                    events,
                    eventIdsAll.ToImmutableList(),
                    eventActivityIdsAll.ToImmutableList(),
                    eventProviderNamesAll.ToImmutableList(),
                    eventTaskNamesAll.ToImmutableList(),
                    eventKeywordNamesAll.ToImmutableList(),
                    debugLogger));

                dispatcher.Dispatch(new EventLogAction.SetEventsLoading(activityId, 0));

                if (action.LogType == EventLogState.LogType.Live)
                {
                    logWatcherService.AddLog(action.LogName, lastEvent?.Bookmark);
                }
            }
            finally
            {
                eventResolver.Dispose();
            }
        }, new CancellationToken());
    }

    [EffectMethod]
    public Task HandleCloseLogAction(EventLogAction.CloseLog action, IDispatcher dispatcher)
    {
        logWatcherService.RemoveLog(action.LogName);

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(EventLogAction.CloseAll))]
    public Task HandleCloseAllAction(IDispatcher dispatcher)
    {
        logWatcherService.RemoveAll();

        return Task.CompletedTask;
    }
}
