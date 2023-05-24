// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using EventLogExpert.Store.FilterPane;

namespace EventLogExpert.Components;

public partial class FilterPane
{
    private readonly FilterDateModel _model = new();

    private FilterDateModel? _availableRange;

    private bool _expandMenu;

    private string MenuState => _expandMenu.ToString().ToLower();

    protected override void OnInitialized()
    {
        AvailableFilterState.StateChanged += (sender, args) =>
        {
            _availableRange = AvailableFilterState.Value.EventDateRange;
            ResetDateModel();
            ApplyDateFilter();
        };

        SettingsState.StateChanged += (sender, args) => { ResetDateModel(); };

        base.OnInitialized();
    }

    private void AddFilter()
    {
        Dispatcher.Dispatch(new FilterPaneAction.AddFilter());
        _expandMenu = true;
    }

    private void ApplyDateFilter()
    {
        FilterDateModel model = new()
        {
            After = _model.After.ToUniversalTime(), 
            Before = _model.Before.ToUniversalTime()
        };

        Dispatcher.Dispatch(new FilterPaneAction.SetFilterDateRange(model));
    }

    private void ResetDateFilter()
    {
        if (_availableRange is null) { return; }

        Dispatcher.Dispatch(new FilterPaneAction.SetFilterDateRange(_availableRange));
        ResetDateModel();
    }

    private void ResetDateModel()
    {
        // Adding 1 minute offset because DateTime input does not include seconds so we don't want to drop events
        _model.After = _availableRange?.After.AddMinutes(-1).ConvertTimeZone(SettingsState.Value.TimeZone) ??
            DateTime.Now;

        _model.Before = _availableRange?.Before.AddMinutes(1).ConvertTimeZone(SettingsState.Value.TimeZone) ??
            DateTime.Now;
    }

    private void ToggleMenu() => _expandMenu = !_expandMenu;
}
