// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using EventLogExpert.Store.EventLog;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.JSInterop;

namespace EventLogExpert.Components;

public partial class EventTable
{
    private Virtualize<DisplayEventModel>? _virtualizeComponent;

    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await JSRuntime.InvokeVoidAsync("enableColumnResize");
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override void OnInitialized()
    {
        IList<DisplayEventModel> lastEventList = EventLogState.Value.Events;

        EventLogState.StateChanged += (s, e) =>
        {
            if (s is State<EventLogState> state && !Equals(state.Value.Events, lastEventList))
            {
                lastEventList = state.Value.Events;
                _virtualizeComponent?.RefreshDataAsync();
            }
        };

        base.OnInitialized();
    }

    private string GetCss(DisplayEventModel @event) => EventLogState.Value.SelectedEvent?.RecordId == @event.RecordId ?
        "table-row selected" : "table-row";

    private IList<DisplayEventModel> GetFilteredEvents()
    {
        if (!FilterPaneState.Value.AppliedFilters.Any())
        {
            return EventLogState.Value.Events;
        }

        return EventLogState.Value.Events
            .Where(e => FilterPaneState.Value.AppliedFilters
                .All(filter => filter.Comparison
                    .Any(comp => comp(e))))
            .ToList();
    }

    private void SelectEvent(DisplayEventModel @event) => Dispatcher.Dispatch(new EventLogAction.SelectEvent(@event));
}
