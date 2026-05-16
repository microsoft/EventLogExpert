// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Components.Modals.Filters;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Filtering.Runtime;
using EventLogExpert.UI.Alerts;
using EventLogExpert.UI.Common.Display;
using EventLogExpert.UI.EventLog;
using EventLogExpert.UI.FilterCache;
using EventLogExpert.UI.FilterLoading;
using EventLogExpert.UI.FilterPane;
using EventLogExpert.UI.Menu;
using EventLogExpert.UI.Modal;
using EventLogExpert.UI.Settings;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using FilterGroupState = EventLogExpert.UI.FilterGroup.FilterGroupState;
using FilterMode = EventLogExpert.Filtering.Runtime.FilterMode;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Components.Sections;

public sealed partial class FilterPane : IDisposable
{
    private readonly DateFilter _model = new();
    private readonly List<FilterDraft> _pendingDrafts = [];

    private ElementReference _addFilterChevronRef;
    private long _addFilterMenuId;
    private bool _canEditDate;
    private TimeZoneInfo _currentTimeZone = TimeZoneInfo.Utc;
    private bool _isFilterListVisible;
    private bool _isGroupPickerVisible;
    private FilterGroupId _selectedGroupId;

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IState<EventLogState> EventLogState { get; init; } = null!;

    [Inject] private IState<FilterCacheState> FilterCacheState { get; init; } = null!;

    [Inject] private IState<FilterGroupState> FilterGroupState { get; init; } = null!;

    [Inject] private IState<FilterLoadingState> FilterLoadingState { get; init; } = null!;

    [Inject] private IFilterPaneCommands FilterPaneCommands { get; init; } = null!;

    [Inject] private IState<FilterPaneState> FilterPaneState { get; init; } = null!;

    private bool HasCachedFilters =>
        !FilterCacheState.Value.FavoriteFilters.IsEmpty || !FilterCacheState.Value.RecentFilters.IsEmpty;

    private bool HasClearableFilters =>
        IsDateFilterVisible || FilterPaneState.Value.Filters.IsEmpty is false || _pendingDrafts.Count > 0;

    private bool HasFilterGroups => !FilterGroupState.Value.Groups.IsEmpty;

    private bool HasFilters =>
        IsDateFilterVisible || _isGroupPickerVisible || FilterPaneState.Value.Filters.IsEmpty is false || _pendingDrafts.Count > 0;

    private bool HasSavableFilters => !FilterPaneState.Value.Filters.IsEmpty;

    private bool IsAddFilterMenuOpen =>
        _addFilterMenuId != 0 && MenuService.ActiveMenuId == _addFilterMenuId && MenuService.ActiveItems is not null;

    private bool IsDateFilterVisible => _canEditDate || FilterPaneState.Value.FilteredDateRange is not null;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private IMenuActionService MenuActions { get; init; } = null!;

    [Inject] private IMenuService MenuService { get; init; } = null!;

    private string MenuState => HasFilters ? _isFilterListVisible.ToString().ToLower() : "false";

    [Inject] private IModalService ModalService { get; init; } = null!;

    [Inject] private ISettingsService Settings { get; init; } = null!;

    public void Dispose()
    {
        Settings.TimeZoneChanged -= UpdateFilterDateTimeZone;
        MenuService.StateChanged -= OnMenuServiceStateChanged;
    }

    protected override void OnInitialized()
    {
        SubscribeToAction<ClearAllFiltersAction>(action =>
        {
            _canEditDate = false;
            _pendingDrafts.Clear();
            _isGroupPickerVisible = false;
            _selectedGroupId = default;
        });

        SubscribeToAction<SetFilterDateRangeSuccessAction>(action =>
        {
            UpdateFilterDate(action.DateFilter);
        });

        Settings.TimeZoneChanged += UpdateFilterDateTimeZone;
        MenuService.StateChanged += OnMenuServiceStateChanged;

        base.OnInitialized();
    }

    private void AddAdvancedFilter()
    {
        _pendingDrafts.Add(new FilterDraft { Mode = FilterMode.Advanced });
        _isFilterListVisible = true;
    }

    private void AddAdvancedFilterFromMenu()
    {
        AddAdvancedFilter();
        StateHasChanged();
    }

    private void AddBasicFilter()
    {
        _pendingDrafts.Add(new FilterDraft { Mode = FilterMode.Basic });
        _isFilterListVisible = true;
    }

    private void AddBasicFilterFromMenu()
    {
        AddBasicFilter();
        StateHasChanged();
    }

    private void AddCachedFilter()
    {
        _pendingDrafts.Add(new FilterDraft { Mode = FilterMode.Cached });
        _isFilterListVisible = true;
    }

    private void AddCachedFilterFromMenu()
    {
        AddCachedFilter();
        StateHasChanged();
    }

    private void AddDateFilter()
    {
        _currentTimeZone = Settings.TimeZoneInfo;

        var (after, before) = EventLogState.Value.ActiveLogs.Values.GetEventDateRange(DateTime.UtcNow);

        _model.After = after.ConvertTimeZone(_currentTimeZone);
        _model.Before = before.ConvertTimeZone(_currentTimeZone);

        _isFilterListVisible = true;
        _canEditDate = true;
    }

    private void AddExclusion()
    {
        _pendingDrafts.Add(new FilterDraft { Mode = FilterMode.Basic, IsExcluded = true });
        _isFilterListVisible = true;
    }

    private void ApplyDateFilter()
    {
        Dispatcher.Dispatch(
            new SetFilterDateRangeAction(
                new DateFilter
                {
                    After = _model.After?.ConvertTimeZoneToUtc(_currentTimeZone),
                    Before = _model.Before?.ConvertTimeZoneToUtc(_currentTimeZone)
                }));

        _canEditDate = false;
    }

    private void ApplyFilterGroupSelection()
    {
        var group = FilterGroupState.Value.Groups.FirstOrDefault(g => g.Id == _selectedGroupId);

        if (group is not null)
        {
            Dispatcher.Dispatch(new ApplyFilterGroupAction(group));
        }

        CancelFilterGroupPicker();
    }

    private IReadOnlyList<MenuItem> BuildAddFilterMenu() =>
    [
        MenuItem.Item("Basic", AddBasicFilterFromMenu),
        MenuItem.Item("Advanced", AddAdvancedFilterFromMenu),
        MenuItem.Item(
            "Cached",
            AddCachedFilterFromMenu,
            isEnabled: HasCachedFilters,
            disabledReason: HasCachedFilters
                ? null
                : "No cached filters yet — apply a Basic or Advanced filter to populate."),
    ];

    private void CancelFilterGroupPicker()
    {
        _isGroupPickerVisible = false;
        _selectedGroupId = default;
    }

    private async Task ClearAllFiltersAsync()
    {
        if (!HasClearableFilters) { return; }

        // Counts everything Clear All actually removes: persisted filters (regardless of IsEnabled),
        // any open date filter row, and any uncommitted pending drafts.
        int count = FilterPaneState.Value.Filters.Count
            + _pendingDrafts.Count
            + (IsDateFilterVisible ? 1 : 0);

        // HasClearableFilters guarantees count >= 1, so the message is always factual.
        string message = count == 1
            ? "Clear 1 filter? This cannot be undone."
            : $"Clear {count} filters? This cannot be undone.";

        bool confirmed = await AlertDialogService.ShowAlert("Clear All Filters", message, "Clear", "Cancel");

        if (confirmed) { Dispatcher.Dispatch(new ClearAllFiltersAction()); }
    }

    private void EditDateFilter() => _canEditDate = true;

    private int GetActiveFilters()
    {
        int count = 0;

        count += FilterPaneState.Value.FilteredDateRange?.IsEnabled is true ? 1 : 0;
        count += FilterPaneState.Value.Filters.Count(filter => filter.IsEnabled);

        return count;
    }

    private string GetGroupName(FilterGroupId id) =>
        FilterGroupState.Value.Groups.FirstOrDefault(g => g.Id == id)?.Name ?? string.Empty;

    private async Task HandleAddFilterChevronKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key is "ArrowDown")
        {
            await OpenAddFilterMenuAtAsync(true);
        }
        else if (e.Key is "ArrowUp")
        {
            await OpenAddFilterMenuAtAsync(false);
        }
    }

    private void HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key is "Enter" or " ")
        {
            ToggleMenu();
        }
    }

    private void HandlePendingDiscard(FilterDraft draft) => _pendingDrafts.Remove(draft);

    private void HandlePendingSave(FilterDraft draft, SavedFilter filter)
    {
        _pendingDrafts.Remove(draft);
        Dispatcher.Dispatch(new SetFilterAction(filter));
    }

    // Marshaled through the renderer dispatcher because StateChanged may fire from arbitrary threads;
    // re-renders so the chevron's aria-expanded reflects open/close state.
    private void OnMenuServiceStateChanged() => _ = InvokeAsync(StateHasChanged);

    private async Task OpenAddFilterMenuAsync() => await OpenAddFilterMenuAtAsync(true);

    private async Task OpenAddFilterMenuAtAsync(bool focusFirst)
    {
        var rect = await JSRuntime.InvokeAsync<MenuAnchorRect>("getMenuElementRect", _addFilterChevronRef);
        MenuService.OpenAt(rect.Left, rect.Bottom, BuildAddFilterMenu(), focusFirst);
        _addFilterMenuId = MenuService.ActiveMenuId;
        StateHasChanged();
    }

    private async Task OpenCachedFiltersModal() => await ModalService.Show<FilterCacheModal, bool>();

    private void OpenFilterGroupPicker()
    {
        // Re-clicking the trigger is a no-op so an in-progress dropdown selection isn't silently
        // overwritten by `Groups.First()` reset. Cancel button is the only way to dismiss the picker.
        if (_isGroupPickerVisible) { return; }

        // Per locked design: trigger is always enabled. Picker shows empty-state copy when no groups
        // exist (rather than disabling the trigger and leaving users without a discovery hint).
        // Pre-select uses the dropdown's display order (alphabetical) so the highlighted entry
        // matches the first row a sighted user sees.
        _isGroupPickerVisible = true;
        _selectedGroupId = HasFilterGroups
            ? FilterGroupState.Value.Groups.OrderBy(g => g.Name).First().Id
            : default;
        _isFilterListVisible = true;
    }

    private async Task OpenFilterGroupsModal() => await ModalService.Show<FilterGroupModal, bool>();

    private void RemoveDateFilter()
    {
        _canEditDate = false;
        Dispatcher.Dispatch(new SetFilterDateRangeAction(null));
    }

    private Task SaveFiltersAsGroupAsync() => !HasSavableFilters ? Task.CompletedTask : MenuActions.SaveFiltersAsGroupAsync();

    private void ToggleDateFilter() => FilterPaneCommands.ToggleFilterDate();

    private void ToggleMenu() => _isFilterListVisible = !_isFilterListVisible;

    private void UpdateFilterDate(DateFilter? updatedDate)
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
