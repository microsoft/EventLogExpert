// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using EventLogExpert.Store.FilterPane;

namespace EventLogExpert.Components;

public partial class FilterPane
{
    private FilterDateModel _model = new();

    private bool _editingDateRange = false;

    private bool _expandMenu;

    private string MenuState => _expandMenu.ToString().ToLower();

    private void AddFilter()
    {
        Dispatcher.Dispatch(new FilterPaneAction.AddFilter());
        _expandMenu = true;
    }

    private void AddDateFilter()
    {
        _model.Before = EventLogState.Value.Events.FirstOrDefault()?.TimeCreated.AddMinutes(1).ConvertTimeZone(SettingsState.Value.TimeZone) ?? DateTime.Now;
        _model.After = EventLogState.Value.Events.LastOrDefault()?.TimeCreated.AddMinutes(-1).ConvertTimeZone(SettingsState.Value.TimeZone) ?? DateTime.Now;
        _editingDateRange = true;
    }

    private void ApplyDateFilter()
    {
        FilterDateModel model = new()
        {
            After = _model.After.ToUniversalTime(), 
            Before = _model.Before.ToUniversalTime()
        };

        Dispatcher.Dispatch(new FilterPaneAction.SetFilterDateRange(model));
        _editingDateRange = false;
    }

    private void RemoveDateFilter()
    {
        Dispatcher.Dispatch(new FilterPaneAction.SetFilterDateRange(null));
        _editingDateRange = false;
    }

    private void EditDateFilter()
    {
        _editingDateRange = true;
    }

    public bool IsEditingDisabled => _editingDateRange == false;

    private void ToggleMenu() => _expandMenu = !_expandMenu;
}
