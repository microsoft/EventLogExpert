// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using EventLogExpert.Store.EventLog;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Linq.Dynamic.Core;

namespace EventLogExpert.Components;

public partial class EventTable
{
    private bool _isDateTimeDescending = true;

    private string IsDateTimeDescending => _isDateTimeDescending.ToString().ToLower();

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
        MaximumStateChangedNotificationsPerSecond = 2;

        base.OnInitialized();
    }

    private string GetCss(DisplayEventModel @event) => EventLogState.Value.SelectedEvent?.RecordId == @event.RecordId ?
        "table-row selected" : "table-row";

    private IList<DisplayEventModel> GetFilteredEvents()
    {
        var filteredEvents = EventLogState.Value.Events.AsQueryable();

        if (FilterPaneState.Value.FilteredDateRange is not null && FilterPaneState.Value.FilteredDateRange.IsEnabled)
        {
            filteredEvents = filteredEvents.Where(e =>
                e.TimeCreated >= FilterPaneState.Value.FilteredDateRange.After &&
                e.TimeCreated <= FilterPaneState.Value.FilteredDateRange.Before);
        }

        if (FilterPaneState.Value.CurrentFilters.Any())
        {
            filteredEvents = filteredEvents.Where(e => FilterPaneState.Value.CurrentFilters
                .Where(filter => filter.IsEnabled && !filter.IsEditing)
                .All(filter => filter.Comparison
                    .Any(comp => comp(e))));
        }

        if (!string.IsNullOrEmpty(FilterPaneState.Value.AdvancedFilter) &&
            FilterPaneState.Value.IsAdvancedFilterEnabled)
        {
            filteredEvents = filteredEvents.Where(FilterPaneState.Value.AdvancedFilter);
        }

        if (!_isDateTimeDescending)
        {
            filteredEvents = filteredEvents.OrderBy(x => x.TimeCreated);
        }

        return filteredEvents.ToList();
    }

    private void SelectEvent(DisplayEventModel @event) => Dispatcher.Dispatch(new EventLogAction.SelectEvent(@event));

    private void ToggleDateTime() => _isDateTimeDescending = !_isDateTimeDescending;
}
