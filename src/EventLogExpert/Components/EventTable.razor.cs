// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using EventLogExpert.Store.EventLog;
using EventLogExpert.Store.FilterPane;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Linq.Dynamic.Core;

namespace EventLogExpert.Components;

public partial class EventTable
{
    private string? _activeLog;
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

    private IList<DisplayEventModel> GetFilteredEvents(string? logName)
    {
        IQueryable<DisplayEventModel> filteredEvents = logName is null
            ? EventLogState.Value.ActiveLogs.Values.SelectMany(l => l.Events).AsQueryable()
            : EventLogState.Value.ActiveLogs.Values.Where(l => l.Name == logName).SelectMany(l => l.Events)
                .AsQueryable();

        int numberOfFilteredEvents = 0;
        int initialNumberOfEvents = filteredEvents.Count();

        if (FilterPaneState.Value.FilteredDateRange is not null && FilterPaneState.Value.FilteredDateRange.IsEnabled)
        {
            filteredEvents = filteredEvents.Where(e =>
                e.TimeCreated >= FilterPaneState.Value.FilteredDateRange.After &&
                e.TimeCreated <= FilterPaneState.Value.FilteredDateRange.Before);
        }

        if (FilterPaneState.Value.CurrentFilters.Any())
        {
            filteredEvents = filteredEvents.AsParallel().Where(e => FilterPaneState.Value.CurrentFilters
                .Where(filter => filter is { IsEnabled: true, IsEditing: false })
                .All(filter => filter.Comparison
                    .Any(comp => comp(e))))
                .AsQueryable();
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
        else if (EventLogState.Value.ActiveLogs.Count > 1 && logName is null)
        {
            // If we only have one log open, the filteredEvents enumerable already
            // has them all in descending order. However, if there's more than one,
            // we need to order them here.
            filteredEvents = filteredEvents.OrderByDescending(x => x.TimeCreated);
        }

        var returnList = filteredEvents.ToList();

        if (returnList.Count != initialNumberOfEvents)
        {
            numberOfFilteredEvents = returnList.Count - initialNumberOfEvents + initialNumberOfEvents;
        }

        if (numberOfFilteredEvents != FilterPaneState.Value.NumberOfFilteredEvents)
        {
            Dispatcher.Dispatch(new FilterPaneAction.SetNumberOfFilteredEvents(numberOfFilteredEvents));
        }

        return returnList;
    }

    private void SelectEvent(DisplayEventModel @event) => Dispatcher.Dispatch(new EventLogAction.SelectEvent(@event));

    private void ToggleDateTime() => _isDateTimeDescending = !_isDateTimeDescending;
}
