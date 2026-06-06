// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Common.Display;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.FilterProgress;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.FilterEditor;
using EventLogExpert.UI.Focus;
using EventLogExpert.UI.Modal;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using FilterMode = EventLogExpert.Filtering.Evaluation.FilterMode;

namespace EventLogExpert.UI.FilterPane;

public sealed partial class FilterPane : IDisposable
{
    internal bool IsFilterSetPickerVisible;
    internal LibraryEntryId SelectedFilterSetId;

    private readonly DateFilter _model = new();
    private readonly List<FilterDraft> _pendingDrafts = [];
    private readonly Dictionary<FilterId, FilterRow?> _rowRefs = new();

    private ElementReference _addFilterButtonRef;
    private ElementReference _addFilterChevronRef;
    private long _addFilterMenuId;
    private bool _canEditDate;
    private TimeZoneInfo _currentTimeZone = TimeZoneInfo.Utc;
    private bool _focusAddButtonAfterRemove;
    private FilterId? _focusTargetAfterRemove;
    private bool _isFilterListVisible;

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IAnnouncementService AnnouncementService { get; init; } = null!;

    [Inject] private IState<EventLogState> EventLogState { get; init; } = null!;

    [Inject] private IFilterLibraryCommands FilterLibraryCommands { get; init; } = null!;

    [Inject] private IState<FilterLibraryState> FilterLibraryState { get; init; } = null!;

    [Inject] private IFilterPaneCommands FilterPaneCommands { get; init; } = null!;

    [Inject] private IState<FilterPaneState> FilterPaneState { get; init; } = null!;

    [Inject] private IState<FilterProgressState> FilterProgressState { get; init; } = null!;

    private bool HasClearableFilters =>
        IsDateFilterVisible || FilterPaneState.Value.Filters.IsEmpty is false || _pendingDrafts.Count > 0;

    private bool HasFilters =>
        IsDateFilterVisible || IsFilterSetPickerVisible || FilterPaneState.Value.Filters.IsEmpty is false || _pendingDrafts.Count > 0;

    private bool HasFilterSets =>
        FilterLibraryState.Value.IsLoaded
        && !FilterLibraryState.Value.LoadError
        && FilterLibraryState.Value.Entries.OfType<LibraryEntryFilterSet>().Any();

    private bool HasRecentFilters =>
        FilterLibraryState.Value.IsLoaded
        && !FilterLibraryState.Value.LoadError
        && FilterLibraryState.Value.Entries.OfType<LibraryEntrySavedFilter>().Any(e => e.IsFavorite || e.LastUsedUtc is not null);

    private bool HasSavableFilters => !FilterPaneState.Value.Filters.IsEmpty;

    private bool IsAddFilterMenuOpen =>
        _addFilterMenuId != 0 && MenuService.ActiveMenuId == _addFilterMenuId && MenuService.ActiveItems is not null;

    private bool IsDateFilterVisible => _canEditDate || FilterPaneState.Value.FilteredDateRange is not null;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private IMenuActionService MenuActions { get; init; } = null!;

    [Inject] private IMenuService MenuService { get; init; } = null!;

    private string MenuState => HasFilters ? _isFilterListVisible.ToString().ToLower() : "false";

    [Inject] private IModalCoordinator ModalCoordinator { get; init; } = null!;

    [Inject] private ISettingsService Settings { get; init; } = null!;

    public void Dispose()
    {
        Settings.TimeZoneChanged -= UpdateFilterDateTimeZone;
        MenuService.StateChanged -= OnMenuServiceStateChanged;
    }

    internal void ApplyFilterSetSelection()
    {
        if (FilterLibraryState.Value.LoadError)
        {
            AnnouncementService.Announce(FilterPaneAnnouncements.LoadFailedRetryViaModal);
            CancelFilterSetPicker();

            return;
        }

        if (!FilterLibraryState.Value.IsLoaded)
        {
            AnnouncementService.Announce(FilterPaneAnnouncements.LoadingTryAgain);
            CancelFilterSetPicker();
            
            return;
        }

        var filterSets = FilterLibraryState.Value.Entries.OfType<LibraryEntryFilterSet>().ToList();

        if (!filterSets.Any(p => p.Id.Equals(SelectedFilterSetId)))
        {
            AnnouncementService.Announce(FilterPaneAnnouncements.SelectedFilterSetMissing);
            SelectedFilterSetId = filterSets
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault()?.Id ?? default;
            
            return;
        }

        FilterLibraryCommands.ApplyEntry(SelectedFilterSetId);
        CancelFilterSetPicker();
    }

    internal IReadOnlyList<MenuItem> BuildAddFilterMenu() =>
    [
        MenuItem.Item("Basic", AddBasicFilterFromMenu),
        MenuItem.Item("Advanced", AddAdvancedFilterFromMenu),
        MenuItem.Item(
            "Recent",
            AddRecentFilterFromMenu,
            isEnabled: HasRecentFilters,
            disabledReason: GetRecentDisabledReason()),
    ];

    internal string? GetRecentDisabledReason()
    {
        if (HasRecentFilters) { return null; }

        if (FilterLibraryState.Value.LoadError) { return FilterPaneAnnouncements.LoadFailedRetryViaModal; }

        return !FilterLibraryState.Value.IsLoaded ?
            FilterPaneAnnouncements.LoadingTryAgain :
            FilterPaneAnnouncements.RecentNoneAvailable;
    }

    internal void OpenFilterSetPicker()
    {
        // Re-clicking the trigger is a no-op so an in-progress dropdown selection isn't silently
        // overwritten by `filterSets.First()` reset. Cancel button is the only way to dismiss the picker.
        if (IsFilterSetPickerVisible) { return; }

        if (FilterLibraryState.Value.LoadError)
        {
            AnnouncementService.Announce(FilterPaneAnnouncements.LoadFailedRetryViaModal);
            return;
        }

        if (!FilterLibraryState.Value.IsLoaded)
        {
            AnnouncementService.Announce(FilterPaneAnnouncements.LoadingTryAgain);
            return;
        }

        IsFilterSetPickerVisible = true;
        SelectedFilterSetId = HasFilterSets
            ? FilterLibraryState.Value.Entries
                .OfType<LibraryEntryFilterSet>()
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .First().Id
            : default;
        _isFilterListVisible = true;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        PruneStaleRowRefs();

        if (_focusTargetAfterRemove is { } targetId
            && _rowRefs.TryGetValue(targetId, out var target)
            && target is not null)
        {
            _focusTargetAfterRemove = null;
            await target.FocusEditAsync();
        }
        else if (_focusAddButtonAfterRemove)
        {
            _focusAddButtonAfterRemove = false;
            await ElementFocus.SafelyAsync(_addFilterButtonRef);
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override void OnInitialized()
    {
        SubscribeToAction<ClearAllFiltersAction>(action =>
        {
            _canEditDate = false;
            _pendingDrafts.Clear();
            IsFilterSetPickerVisible = false;
            SelectedFilterSetId = default;
        });

        SubscribeToAction<SetFilterDateRangeSuccessAction>(action =>
        {
            UpdateFilterDate(action.DateFilter);
        });

        Settings.TimeZoneChanged += UpdateFilterDateTimeZone;
        MenuService.StateChanged += OnMenuServiceStateChanged;

        base.OnInitialized();
    }

    private static string FormatFilterSetDetail(LibraryEntryFilterSet set)
    {
        var detail = $"{set.Filters.Count} filter{(set.Filters.Count == 1 ? string.Empty : "s")}";

        if (set.Tags.Count > 0)
        {
            detail = $"{detail} · {string.Join(", ", set.Tags)}";
        }

        return detail;
    }

    private static string FormatFilterSetLabel(LibraryEntryFilterSet set) =>
        $"{set.Name} ({FormatFilterSetDetail(set)})";

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

    private void AddRecentFilter()
    {
        _pendingDrafts.Add(new FilterDraft { Mode = FilterMode.Cached });
        _isFilterListVisible = true;
    }

    private void AddRecentFilterFromMenu()
    {
        AddRecentFilter();
        StateHasChanged();
    }

    private void ApplyDateFilter()
    {
        FilterPaneCommands.SetFilterDateRange(
            new DateFilter
            {
                After = _model.After?.ConvertTimeZoneToUtc(_currentTimeZone),
                Before = _model.Before?.ConvertTimeZoneToUtc(_currentTimeZone)
            });

        _canEditDate = false;
    }

    private void CancelFilterSetPicker()
    {
        IsFilterSetPickerVisible = false;
        SelectedFilterSetId = default;
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

        if (confirmed) { FilterPaneCommands.ClearAllFilters(); }
    }

    private void EditDateFilter() => _canEditDate = true;

    private int GetActiveFilters()
    {
        int count = 0;

        count += FilterPaneState.Value.FilteredDateRange?.IsEnabled is true ? 1 : 0;
        count += FilterPaneState.Value.Filters.Count(filter => filter.IsEnabled);

        return count;
    }

    private string GetFilterSetName(LibraryEntryId id)
    {
        var set = FilterLibraryState.Value.Entries.OfType<LibraryEntryFilterSet>().FirstOrDefault(p => p.Id.Equals(id));

        return set is null ? string.Empty : FormatFilterSetLabel(set);
    }

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

    private void HandlePendingDiscard(FilterDraft draft)
    {
        _pendingDrafts.Remove(draft);

        var target = FilterPaneFocus.ComputeFocusTargetAfterPendingDiscard(
            FilterPaneState.Value.Filters,
            IsFocusable);

        _focusTargetAfterRemove = target;
        _focusAddButtonAfterRemove = target is null;
    }

    private void HandlePendingSave(FilterDraft draft, SavedFilter filter)
    {
        _pendingDrafts.Remove(draft);
        FilterPaneCommands.SetFilter(filter);
    }

    private void HandleRemovedFilter(FilterId removedId)
    {
        var target = FilterPaneFocus.ComputeFocusTargetAfterRemove(
            FilterPaneState.Value.Filters,
            removedId,
            IsFocusable);

        _focusTargetAfterRemove = target;
        _focusAddButtonAfterRemove = target is null;
    }

    private bool IsFocusable(FilterId id) =>
        _rowRefs.TryGetValue(id, out var row) && row is not null && !row.IsEditing;

    // Marshaled through the renderer dispatcher because StateChanged may fire from arbitrary threads;
    // re-renders so the chevron's aria-expanded reflects open/close state.
    private void OnMenuServiceStateChanged() => _ = InvokeAsync(StateHasChanged);

    private void OnRowDisposed(FilterRow row)
    {
        FilterId? match = null;

        foreach (var kvp in _rowRefs)
        {
            if (ReferenceEquals(kvp.Value, row))
            {
                match = kvp.Key;
                break;
            }
        }

        if (match is { } id) { _rowRefs.Remove(id); }
    }

    private async Task OpenAddFilterMenuAsync() => await OpenAddFilterMenuAtAsync(true);

    private async Task OpenAddFilterMenuAtAsync(bool focusFirst)
    {
        var rect = await JSRuntime.InvokeAsync<MenuAnchorRect>("getMenuElementRect", _addFilterChevronRef);
        MenuService.OpenAt(rect.Left, rect.Bottom, BuildAddFilterMenu(), focusFirst);
        _addFilterMenuId = MenuService.ActiveMenuId;
        StateHasChanged();
    }

    private Task OpenFilterLibraryAsync() => ModalCoordinator.OpenFilterLibraryAsync();

    private void PruneStaleRowRefs()
    {
        if (_rowRefs.Count == 0) { return; }

        var liveFilters = FilterPaneState.Value.Filters;

        if (liveFilters.Count == 0)
        {
            _rowRefs.Clear();
            return;
        }

        var liveIds = liveFilters.Select(f => f.Id).ToHashSet();

        var stale = _rowRefs
            .Where(kvp => kvp.Value is null || !liveIds.Contains(kvp.Key))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var id in stale) { _rowRefs.Remove(id); }
    }

    private void RemoveDateFilter()
    {
        _canEditDate = false;
        FilterPaneCommands.SetFilterDateRange(null);
    }

    private Task SaveFiltersAsFilterSetAsync() => !HasSavableFilters ? Task.CompletedTask : MenuActions.SaveFiltersAsFilterSetAsync();

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
