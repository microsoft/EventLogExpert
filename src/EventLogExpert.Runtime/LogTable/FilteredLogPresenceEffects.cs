// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.EventLog;
using Fluxor;
using CloseAllLogsAction = EventLogExpert.Runtime.EventLog.CloseAllLogsAction;

namespace EventLogExpert.Runtime.LogTable;

internal sealed class FilteredLogPresenceEffects(FilteredLogPresenceCoordinator coordinator)
{
    private readonly FilteredLogPresenceCoordinator _coordinator = coordinator;

    [EffectMethod(typeof(ApplyFilterAction))]
    public Task HandleApplyFilter(IDispatcher dispatcher)
    {
        _coordinator.MarkFilterChanged();

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(CloseAllLogsAction))]
    public Task HandleCloseAll(IDispatcher dispatcher)
    {
        _coordinator.DiscardAll();

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleCloseLog(CloseLogAction action, IDispatcher dispatcher)
    {
        _coordinator.Discard(action.LogId);

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleIngestRawEvents(IngestRawEventsAction action, IDispatcher dispatcher)
    {
        if (action.Mode == RawIngestMode.Replace)
        {
            foreach (var logId in action.EventsByLog.Keys) { _coordinator.MarkRebuilt(logId); }

            return Task.CompletedTask;
        }

        _coordinator.MarkAppended(action.EventsByLog.Keys);

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleLoadEvents(LoadEventsAction action, IDispatcher dispatcher)
    {
        _coordinator.MarkRebuilt(action.LogData.Id);

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleLoadEventsPartial(LoadEventsPartialAction action, IDispatcher dispatcher)
    {
        _coordinator.MarkAppended([action.LogData.Id]);

        return Task.CompletedTask;
    }
}
