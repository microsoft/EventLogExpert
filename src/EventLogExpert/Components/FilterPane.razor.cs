// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Shared.Components.Filters;
using EventLogExpert.UI;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.FilterPane;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Components;

public sealed partial class FilterPane : IDisposable
{
    private readonly FilterDateModel _model = new();
    private readonly List<FilterEditorModel> _pendingDrafts = [];

    private bool _canEditDate;
    private TimeZoneInfo _currentTimeZone = TimeZoneInfo.Utc;
    private bool _isFilterListVisible;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IState<EventLogState> EventLogState { get; init; } = null!;

    [Inject] private IState<FilterPaneState> FilterPaneState { get; init; } = null!;

    [Inject] private IModalService ModalService { get; init; } = null!;

    private bool HasFilters =>
        IsDateFilterVisible || FilterPaneState.Value.Filters.IsEmpty is false || _pendingDrafts.Count > 0;

    private bool IsDateFilterVisible => _canEditDate || FilterPaneState.Value.FilteredDateRange is not null;

    private string MenuState => HasFilters ? _isFilterListVisible.ToString().ToLower() : "false";

    [Inject] private ISettingsService Settings { get; init; } = null!;

    public void Dispose() => Settings.TimeZoneChanged -= UpdateFilterDateTimeZone;

    protected override void OnInitialized()
    {
        SubscribeToAction<FilterPaneAction.ClearAllFilters>(action =>
        {
            _canEditDate = false;
            _pendingDrafts.Clear();
        });

        SubscribeToAction<FilterPaneAction.SetFilterDateRangeSuccess>(action =>
        {
            UpdateFilterDate(action.FilterDateModel);
        });

        Settings.TimeZoneChanged += UpdateFilterDateTimeZone;

        base.OnInitialized();
    }

    private void AddAdvancedFilter()
    {
        _pendingDrafts.Add(new FilterEditorModel { FilterType = FilterType.Advanced });
        _isFilterListVisible = true;
    }

    private void AddBasicFilter()
    {
        _pendingDrafts.Add(new FilterEditorModel { FilterType = FilterType.Basic });
        _isFilterListVisible = true;
    }

    private async Task AddCachedFilter() => await ModalService.Show<FilterCacheModal, bool>();

    private void AddDateFilter()
    {
        _currentTimeZone = Settings.TimeZoneInfo;

        long ticksPerHour = TimeSpan.FromHours(1).Ticks;

        // TODO: This computes intersection bounds (latest oldest / earliest newest) across active logs,
        // not the global envelope. Same logic is duplicated in FilterPaneEffects.HandleSetFilterDateRange.
        // Lock down intent with tests across both call sites and resolve intersection-vs-envelope.
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
            .ConvertTimeZone(_currentTimeZone);

        // Round up to the nearest hour for the latest event
        _model.Before = new DateTime((mostRecentEventTicks + ticksPerHour - 1) / ticksPerHour * ticksPerHour, DateTimeKind.Utc)
            .ConvertTimeZone(_currentTimeZone);

        _isFilterListVisible = true;
        _canEditDate = true;
    }

    private void AddExclusion()
    {
        _pendingDrafts.Add(new FilterEditorModel { FilterType = FilterType.Basic, IsExcluded = true });
        _isFilterListVisible = true;
    }

    private void ApplyDateFilter()
    {
        Dispatcher.Dispatch(
            new FilterPaneAction.SetFilterDateRange(
                new FilterDateModel
                {
                    After = _model.After?.ConvertTimeZoneToUtc(_currentTimeZone),
                    Before = _model.Before?.ConvertTimeZoneToUtc(_currentTimeZone)
                }));

        _canEditDate = false;
    }

    private void EditDateFilter() => _canEditDate = true;

    private int GetActiveFilters()
    {
        int count = 0;

        count += FilterPaneState.Value.FilteredDateRange?.IsEnabled is true ? 1 : 0;
        count += FilterPaneState.Value.Filters.Count(filter => filter.IsEnabled);

        return count;
    }

    private void HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key is "Enter" or " ")
        {
            ToggleMenu();
        }
    }

    private void HandlePendingDiscard(FilterEditorModel draft) => _pendingDrafts.Remove(draft);

    private void HandlePendingSave(FilterEditorModel draft, FilterModel filter)
    {
        _pendingDrafts.Remove(draft);
        Dispatcher.Dispatch(new FilterPaneAction.SetFilter(filter));
    }

    private void RemoveDateFilter()
    {
        _canEditDate = false;
        Dispatcher.Dispatch(new FilterPaneAction.SetFilterDateRange(null));
    }

    private void ToggleDateFilter() => Dispatcher.Dispatch(new FilterPaneAction.ToggleFilterDate());

    private void ToggleMenu() => _isFilterListVisible = !_isFilterListVisible;

    private void UpdateFilterDate(FilterDateModel? updatedDate)
    {
        _model.Before = updatedDate?.Before?.ConvertTimeZone(_currentTimeZone);
        _model.After = updatedDate?.After?.ConvertTimeZone(_currentTimeZone);
    }

    private void UpdateFilterDateTimeZone(object? sender, TimeZoneInfo timeZoneInfo)
    {
        _model.Before = _model.Before is not null ?
            TimeZoneInfo.ConvertTime(_model.Before.Value, _currentTimeZone, timeZoneInfo) : null;

        _model.After = _model.After is not null ?
            TimeZoneInfo.ConvertTime(_model.After.Value, _currentTimeZone, timeZoneInfo) : null;

        _currentTimeZone = timeZoneInfo;
    }
}
