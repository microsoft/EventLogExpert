// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using EventLogExpert.Store.FilterPane;
using Fluxor;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Components;

public partial class FilterPane
{
    private readonly FilterDateModel _model = new();

    private FilterDateModel? _availableRange;

    private bool _expandMenu;

    [Inject] private IStateSelection<AvailableFilterState, FilterDateModel> AvailableFilterDates { get; set; } = null!;

    private string MenuState => _expandMenu.ToString().ToLower();

    protected override void OnInitialized()
    {
        AvailableFilterDates.Select(x => x.EventDateRange);

        AvailableFilterDates.SelectedValueChanged += (sender, args) =>
        {
            _availableRange = AvailableFilterState.Value.EventDateRange;
            ResetDateModel();
            ApplyDateFilter();
        };

        // Temp: Will reuse this to trigger filters to run anytime a new event log is loaded
        //AvailableFilterState.StateChanged += (sender, args) => { ResetDateFilter(); };
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
        _model.After = _availableRange?.After.ConvertTimeZone(SettingsState.Value.TimeZone) ?? DateTime.Now;
        _model.Before = _availableRange?.Before.ConvertTimeZone(SettingsState.Value.TimeZone) ?? DateTime.Now;
    }

    private void ToggleMenu() => _expandMenu = !_expandMenu;
}
