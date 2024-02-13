// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.UI.Store.EventTable;

public sealed class EventTableEffects(IState<EventTableState> eventTableState)
{
    [EffectMethod(typeof(EventTableAction.UpdateDisplayedEvents))]
    public Task HandleUpdateDisplayedEvents(IDispatcher dispatcher)
    {
        if (eventTableState.Value.EventTables.Any(table => table.IsLoading))
        {
            return Task.CompletedTask;
        }

        dispatcher.Dispatch(new EventTableAction.UpdateCombinedEvents());

        return Task.CompletedTask;
    }
}
