// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using EventLogExpert.Store.EventLog;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Linq.Dynamic.Core;

namespace EventLogExpert.Components;

public partial class EventTable
{
    private double _contextMenuLeft = 0;

    private double _contextMenuTop = 0;

    private int _contextMenuId = 0;

    private bool _isContextMenuVisible = false;

    private bool _isDateTimeDescending = true;

    private string IsDateTimeDescending => _isDateTimeDescending.ToString().ToLower();

    private ElementReference _contextMenu;

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

        if (FilterPaneState.Value.AppliedFilters.Any())
        {
            filteredEvents = filteredEvents.Where(e => FilterPaneState.Value.AppliedFilters
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

    private string GetContextMenuStyle()
    {
        if (!_isContextMenuVisible)
        {
            return "display: none;";
        }
        else
        {
            return $"display: block; position: absolute; left: {_contextMenuLeft}px; top: {_contextMenuTop}px;";
        }
    }

    private void HideContextMenu()
    {
        _isContextMenuVisible = false;
    }

    private void ShowIdContextMenu(MouseEventArgs args, int id)
    {
        _contextMenuId = id;
        _contextMenuLeft = args.PageX;
        _contextMenuTop = args.PageY;
        _isContextMenuVisible = true;
    }

    private void ToggleDateTime() => _isDateTimeDescending = !_isDateTimeDescending;
}
