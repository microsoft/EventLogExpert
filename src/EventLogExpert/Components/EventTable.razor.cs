// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using EventLogExpert.Store.EventLog;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EventLogExpert.Components;

public partial class EventTable
{
    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await JSRuntime.InvokeVoidAsync("enableColumnResize");
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    private string GetCss(DisplayEventModel @event) => EventLogState.Value.SelectedEvent?.RecordId == @event.RecordId ?
        "table-row selected" : "table-row";

    private IList<DisplayEventModel> GetFilteredEvents() => EventLogState.Value.Events
        .Where(e => e.TimeCreated >= FilterPaneState.Value.FilteredDateRange.After &&
            e.TimeCreated <= FilterPaneState.Value.FilteredDateRange.Before)
        .Where(e => FilterPaneState.Value.AppliedFilters
            .All(filter => filter.Comparison
                .Any(comp => comp(e))))
        .ToList();

    private void SelectEvent(DisplayEventModel @event) => Dispatcher.Dispatch(new EventLogAction.SelectEvent(@event));
}
