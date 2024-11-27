// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.FilterCache;
using EventLogExpert.UI.Store.FilterPane;
using EventLogExpert.UI.Store.Settings;
using Fluxor;
using Microsoft.AspNetCore.Components;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Components;

public sealed partial class FilterPane
{
    private readonly FilterDateModel _model = new() { TimeZoneInfo = TimeZoneInfo.Utc };

    private bool _canEditDate;
    private bool _isFilterListVisible;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IState<EventLogState> EventLogState { get; init; } = null!;

    [Inject] private IState<FilterPaneState> FilterPaneState { get; init; } = null!;

    private bool HasFilters => IsDateFilterVisible || FilterPaneState.Value.Filters.IsEmpty is false;

    private bool IsDateFilterVisible => _canEditDate || FilterPaneState.Value.FilteredDateRange is not null;

    private string MenuState => HasFilters ? _isFilterListVisible.ToString().ToLower() : "false";

    [Inject] private IState<SettingsState> SettingsState { get; init; } = null!;

    [Inject] private IStateSelection<SettingsState, string> TimeZoneState { get; init; } = null!;

    protected override void OnInitialized()
    {
        SubscribeToAction<FilterPaneAction.ClearAllFilters>(action => { _canEditDate = false; });

        TimeZoneState.Select(x => x.Config.TimeZoneId);
        TimeZoneState.SelectedValueChanged += (sender, args) => { UpdateFilterDateModel(); };

        base.OnInitialized();
    }

    private void AddAdvancedFilter()
    {
        Dispatcher.Dispatch(new FilterPaneAction.AddFilter(new FilterModel
        {
            FilterType = FilterType.Advanced,
            IsEditing = true
        }));

        _isFilterListVisible = true;
    }

    private void AddBasicFilter()
    {
        Dispatcher.Dispatch(new FilterPaneAction.AddFilter(new FilterModel
        {
            FilterType = FilterType.Basic,
            IsEditing = true
        }));

        _isFilterListVisible = true;
    }

    private void AddCachedFilter() => Dispatcher.Dispatch(new FilterCacheAction.OpenMenu());

    private void AddDateFilter()
    {
        _model.TimeZoneInfo = SettingsState.Value.Config.TimeZoneInfo;

        long ticksPerHour = TimeSpan.FromHours(1).Ticks;

        long oldestEventTicks =
            (EventLogState.Value.ActiveLogs.Values.Select(log => log.Events.LastOrDefault()?.TimeCreated)
                    .Order()
                    .LastOrDefault() ??
                DateTime.UtcNow)
            .Ticks;

        long mostRecentEventTicks =
            (EventLogState.Value.ActiveLogs.Values.Select(log => log.Events.FirstOrDefault()?.TimeCreated)
                    .Order()
                    .FirstOrDefault() ??
                DateTime.UtcNow)
            .Ticks;

        // Round down to the nearest hour for the earliest event
        _model.After = new DateTime(oldestEventTicks / ticksPerHour * ticksPerHour, DateTimeKind.Utc)
            .ConvertTimeZone(SettingsState.Value.Config.TimeZoneInfo);

        // Round up to the nearest hour for the latest event
        _model.Before = new DateTime(((mostRecentEventTicks + ticksPerHour - 1) / ticksPerHour) * ticksPerHour, DateTimeKind.Utc)
            .ConvertTimeZone(SettingsState.Value.Config.TimeZoneInfo);

        _isFilterListVisible = true;
        _canEditDate = true;
    }

    private void AddExclusion()
    {
        Dispatcher.Dispatch(new FilterPaneAction.AddFilter(new FilterModel
        {
            FilterType = FilterType.Basic,
            IsEditing = true,
            IsExcluded = true
        }));

        _isFilterListVisible = true;
    }

    private void ApplyDateFilter()
    {
        FilterDateModel model = new()
        {
            After = _model.After.ConvertTimeZoneToUtc(SettingsState.Value.Config.TimeZoneInfo),
            Before = _model.Before.ConvertTimeZoneToUtc(SettingsState.Value.Config.TimeZoneInfo)
        };

        Dispatcher.Dispatch(new FilterPaneAction.SetFilterDateRange(model));

        _canEditDate = false;
    }

    private void EditDateFilter() => _canEditDate = true;

    private int GetActiveFilters()
    {
        int count = 0;

        count += FilterPaneState.Value.FilteredDateRange?.IsEnabled is true ? 1 : 0;
        count += FilterPaneState.Value.Filters.Count(filter => filter is { IsEnabled: true, IsEditing: false });

        return count;
    }

    private void RemoveDateFilter()
    {
        _canEditDate = false;
        Dispatcher.Dispatch(new FilterPaneAction.SetFilterDateRange(null));
    }

    private void ToggleDateFilter() => Dispatcher.Dispatch(new FilterPaneAction.ToggleFilterDate());

    private void ToggleMenu() => _isFilterListVisible = !_isFilterListVisible;

    private void UpdateFilterDateModel()
    {
        var temp = _model.TimeZoneInfo;
        _model.TimeZoneInfo = SettingsState.Value.Config.TimeZoneInfo;

        _model.Before = TimeZoneInfo.ConvertTime(_model.Before, temp, _model.TimeZoneInfo);
        _model.After = TimeZoneInfo.ConvertTime(_model.After, temp, _model.TimeZoneInfo);
    }
}
