// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using EventLogExpert.Store.FilterPane;

namespace EventLogExpert.Components;

public partial class FilterPane
{
    private readonly FilterDateModel _model = new();

    private bool _canEditDate = true;
    private bool _isDateFilterVisible;
    private bool _isFilterListVisible;

    private string MenuState
    {
        get
        {
            if (FilterPaneState.Value.CurrentFilters.Any() || _isDateFilterVisible)
            {
                return _isFilterListVisible.ToString().ToLower();
            }

            return "false";
        }
    }

    private void AddDateFilter()
    {
        _isFilterListVisible = true;
        _canEditDate = true;

        // Offset by 1 minute to make sure we don't drop events
        // since HTML input DateTime does not go lower than minutes
        _model.Before = EventLogState.Value.Events.FirstOrDefault()?.TimeCreated
                .AddMinutes(1).ConvertTimeZone(SettingsState.Value.TimeZone) ??
            DateTime.Now;

        _model.After = EventLogState.Value.Events.LastOrDefault()?.TimeCreated
                .AddMinutes(-1).ConvertTimeZone(SettingsState.Value.TimeZone) ??
            DateTime.Now;

        _isDateFilterVisible = true;
    }

    private void AddFilter()
    {
        Dispatcher.Dispatch(new FilterPaneAction.AddFilter());
        _isFilterListVisible = true;
    }

    private void ApplyDateFilter()
    {
        FilterDateModel model = new()
        {
            After = _model.After.ToUniversalTime(), Before = _model.Before.ToUniversalTime()
        };

        Dispatcher.Dispatch(new FilterPaneAction.SetFilterDateRange(model));
        _canEditDate = false;
    }

    private void RemoveDateFilter()
    {
        Dispatcher.Dispatch(new FilterPaneAction.SetFilterDateRange(null));
        _isDateFilterVisible = false;
    }

    private void EditDateFilter() => _canEditDate = true;

    private void ToggleMenu() => _isFilterListVisible = !_isFilterListVisible;
}
