// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Runtime.EventLog;

internal sealed class FilteringEffects(
    IState<EventLogState> eventLogState,
    LiveTailIngestCoordinator liveTailCoordinator,
    XmlReloadCoordinator xmlReloadCoordinator)
{
    private readonly IState<EventLogState> _eventLogState = eventLogState;
    private readonly LiveTailIngestCoordinator _liveTailCoordinator = liveTailCoordinator;
    private readonly XmlReloadCoordinator _xmlReloadCoordinator = xmlReloadCoordinator;

    [EffectMethod]
    public Task HandleAddEvent(AddEventAction action, IDispatcher dispatcher)
    {
        if (!_eventLogState.Value.ContinuouslyUpdate ||
            !_eventLogState.Value.OpenLogs.TryGetValue(action.NewEvent.OwningLog, out var owningLog))
        {
            return Task.CompletedTask;
        }

        _liveTailCoordinator.Enqueue(owningLog.Id, action.NewEvent);

        return Task.CompletedTask;
    }

    [EffectMethod]
    public async Task HandleApplyFilter(ApplyFilterAction action, IDispatcher dispatcher)
    {
        PendingXmlReload pendingReload = _xmlReloadCoordinator.Resolve(action.Filter);

        if (pendingReload.IsNeeded)
        {
            await _xmlReloadCoordinator.ReloadAsync(pendingReload, dispatcher);
        }
    }

    [EffectMethod]
    public Task HandleSetContinuouslyUpdate(SetContinuouslyUpdateAction action, IDispatcher dispatcher)
    {
        if (action.ContinuouslyUpdate)
        {
            LogReloadEffects.ProcessNewEventBuffer(_eventLogState.Value, dispatcher);
        }
        else
        {
            _liveTailCoordinator.Flush();
        }

        return Task.CompletedTask;
    }
}
