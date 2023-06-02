// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Helpers;
using EventLogExpert.Library.Models;
using EventLogExpert.Store.EventLog;
using EventLogExpert.Store.FilterPane;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Collections.Immutable;
using System.Linq.Dynamic.Core;
using static EventLogExpert.Store.EventLog.EventLogState;

namespace EventLogExpert.Components;

public partial class EventTable
{
    private string? _activeLog;
    private bool _isDateTimeDescending = true;

    [Inject] private IStateSelection<EventLogState, IImmutableDictionary<string, EventLogData>>
        ActiveLogState { get; set; } = null!;

    private string IsDateTimeDescending => _isDateTimeDescending.ToString().ToLower();

    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await JSRuntime.InvokeVoidAsync("registerTableColumnResizers");
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override void OnInitialized()
    {
        MaximumStateChangedNotificationsPerSecond = 2;

        ActiveLogState.Select(s => s.ActiveLogs);

        ActiveLogState.StateChanged += async (sender, activeLog) =>
        {
            await JSRuntime.InvokeVoidAsync("registerTableColumnResizers");
        };

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
            filteredEvents = filteredEvents.AsParallel()
                .Where(e => FilterPaneState.Value.CurrentFilters
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
        else if (EventLogState.Value.ActiveLogs.Count > 1)
        {
            // If more than one log is loaded we need to make sure they are orderd by date and not record id
            filteredEvents = filteredEvents.OrderByDescending(x => x.TimeCreated);
        }
        else
        {
            // AsParallel puts events out of order so make sure we are still in order
            filteredEvents = filteredEvents.OrderByDescending(x => x.RecordId);
        }

        var returnList = filteredEvents.ToList();

        if (returnList.Count != initialNumberOfEvents)
        {
            numberOfFilteredEvents = returnList.Count - initialNumberOfEvents + initialNumberOfEvents;
        }

        if (_activeLog == logName && numberOfFilteredEvents != FilterPaneState.Value.NumberOfFilteredEvents)
        {
            Dispatcher.Dispatch(new FilterPaneAction.SetNumberOfFilteredEvents(numberOfFilteredEvents));
        }

        return returnList;
    }

    private string GetLevelClass(SeverityLevel? level)
    {
        switch (level)
        {
            case SeverityLevel.Error:
                return "bi bi-exclamation-circle error";
            case SeverityLevel.Warning:
                return "bi bi-exclamation-triangle warning";
            case SeverityLevel.Information:
                return "bi bi-info-circle";
            default:
                return "";
        }
    }

    private void SelectEvent(DisplayEventModel @event) => Dispatcher.Dispatch(new EventLogAction.SelectEvent(@event));

    private void ToggleDateTime() => _isDateTimeDescending = !_isDateTimeDescending;
}
