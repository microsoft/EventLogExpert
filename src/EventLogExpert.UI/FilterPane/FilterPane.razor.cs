// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Common.Display;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Scenarios;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.Scenarios.Catalog;
using EventLogExpert.UI.Common.Interop;
using EventLogExpert.UI.FilterEditor;
using EventLogExpert.UI.Focus;
using EventLogExpert.UI.Inputs;
using EventLogExpert.UI.Menu;
using EventLogExpert.UI.Modal;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Collections.Immutable;
using System.Security;
using FilterMode = EventLogExpert.Filtering.Evaluation.FilterMode;

namespace EventLogExpert.UI.FilterPane;

public sealed partial class FilterPane
{
    internal bool IsFilterSetPickerVisible;
    internal bool IsScenarioPickerVisible;
    internal LibraryEntryId SelectedFilterSetId;
    internal ScenarioGroup? SelectedScenarioGroup;
    internal string? SelectedScenarioId;

    private readonly List<string> _filterSetTags = [];
    private readonly List<FilterDraft> _pendingDrafts = [];
    private readonly Dictionary<FilterId, FilterRow?> _rowRefs = new();

    private Button? _addFilterButton;
    private Button? _addFilterChevron;
    private long _addFilterMenuId;
    private ScenarioAuthoringRowContext? _authoringContext;
    private bool _canEditDate;
    private ScenarioClipboardExporter _clipboardExporter = null!;
    private TimeZoneInfo _currentTimeZone = TimeZoneInfo.Utc;
    private volatile bool _disposed;
    private EditContext _editContext = null!;
    private ElementReference _filterPaneRootRef;
    private bool _focusAddButtonAfterRemove;
    private FilterId? _focusTargetAfterRemove;
    private bool _isFilterListVisible;
    private IJSObjectReference? _menuAnchorModule;
    private DateFilter _model = new();
    private IReadOnlyList<IGrouping<ScenarioGroup, ScenarioDefinition>> _scenarioMatchGroups = [];
    private ImmutableHashSet<string>? _scenarioMatchSource;
    private IJSObjectReference? _scrollSuppressorModule;

    internal IReadOnlyList<string> AvailableFilterSetTags =>
        AvailableTagsForSets([.. LibraryEntries.Current.OfType<LibraryEntryFilterSet>()]);

    internal bool ScenarioAuthoringEnabled => ScenarioAuthoringOptions.Enabled;

    internal IReadOnlyList<LibraryEntryFilterSet> VisibleFilterSets =>
        FilterSetsByTags(
            [.. LibraryEntries.Current.OfType<LibraryEntryFilterSet>()],
            _filterSetTags,
            SelectedFilterSetId);

    [Inject] private IActiveFiltersSource ActiveFilters { get; init; } = null!;

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IAnnouncementService AnnouncementService { get; init; } = null!;

    [Inject] private IClearAllFiltersNotifier ClearAllFiltersNotifier { get; init; } = null!;

    [Inject] private IClipboardService ClipboardService { get; init; } = null!;

    private ScenarioGroup? EffectiveScenarioGroup =>
        SelectedScenarioGroup is { } group && ScenarioMatchGroups.Any(match => match.Key == group)
            ? group
            : ScenarioMatchGroups.FirstOrDefault()?.Key;

    [Inject] private IEventLogQueries EventLogQueries { get; init; } = null!;

    [Inject] private IFilePickerService FilePickerService { get; init; } = null!;

    [Inject] private IFilterLibraryCommands FilterLibraryCommands { get; init; } = null!;

    [Inject] private IFilterPaneCommands FilterPaneCommands { get; init; } = null!;

    [Inject] private IFilteredDateRangeSource FilteredDateRange { get; init; } = null!;

    private bool HasClearableFilters =>
        IsDateFilterVisible || ActiveFilters.Current.IsEmpty is false || _pendingDrafts.Count > 0;

    private bool HasEnabledFilters => ActiveFilters.Current.Any(filter => filter.IsEnabled);

    private bool HasFilterSets =>
        LibraryLoadStatus.Current is { IsLoaded: true, LoadError: false }
        && LibraryEntries.Current.OfType<LibraryEntryFilterSet>().Any();

    private bool HasFilters =>
        IsDateFilterVisible ||
        IsFilterSetPickerVisible ||
        IsScenarioPickerVisible ||
        ActiveFilters.Current.IsEmpty is false ||
        _pendingDrafts.Count > 0;

    private bool HasRecentFilters =>
        LibraryLoadStatus.Current is { IsLoaded: true, LoadError: false }
        && LibraryEntries.Current.OfType<LibraryEntrySavedFilter>().Any(e => e.IsFavorite || e.LastUsedUtc is not null);

    private bool HasSavableFilters => !ActiveFilters.Current.IsEmpty;

    private bool IsAddFilterMenuOpen =>
        _addFilterMenuId != 0 && MenuService.ActiveMenuId == _addFilterMenuId && MenuService.ActiveItems is not null;

    private bool IsDateFilterVisible => _canEditDate || FilteredDateRange.Current is not null;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private ILibraryEntriesSource LibraryEntries { get; init; } = null!;

    [Inject] private ILibraryLoadStatusSource LibraryLoadStatus { get; init; } = null!;

    [Inject] private ILoadedLogNamesSource LoadedLogNames { get; init; } = null!;

    [Inject] private IMenuActionService MenuActions { get; init; } = null!;

    [Inject] private IMenuService MenuService { get; init; } = null!;

    private string MenuState => HasFilters ? _isFilterListVisible.ToString().ToLower() : "false";

    [Inject] private IModalCoordinator ModalCoordinator { get; init; } = null!;

    private DateTime? ModelAfter
    {
        get => _model.After;
        set => _model = _model with { After = value };
    }

    private DateTime? ModelBefore
    {
        get => _model.Before;
        set => _model = _model with { Before = value };
    }

    [Inject] private IOpenLogsPresenceSource OpenLogsPresence { get; init; } = null!;

    private ScenarioDefinition? ResolvedScenario =>
        VisibleScenarioMatches.FirstOrDefault(match => match.Id == SelectedScenarioId)
            ?? VisibleScenarioMatches.FirstOrDefault();

    [Inject] private IScenarioApplyService ScenarioApplyService { get; init; } = null!;

    [Inject] private ScenarioAuthoringOptions ScenarioAuthoringOptions { get; init; } = null!;

    [Inject] private IScenarioAuthoringService ScenarioAuthoringService { get; init; } = null!;

    private IReadOnlyList<IGrouping<ScenarioGroup, ScenarioDefinition>> ScenarioMatchGroups
    {
        get
        {
            var names = LoadedLogNames.Current;

            if (ReferenceEquals(names, _scenarioMatchSource))
            {
                return _scenarioMatchGroups;
            }

            _scenarioMatchSource = names;
            _scenarioMatchGroups =
            [
                .. ScenarioQueryService
                        .GetInAppScenarios(names)
                        .GroupBy(scenario => scenario.Group)
                        .OrderBy(group => group.Key)
            ];

            return _scenarioMatchGroups;
        }
    }

    private IReadOnlyList<ScenarioDefinition> ScenarioMatches => [.. ScenarioMatchGroups.SelectMany(group => group)];

    [Inject] private IScenarioQueryService ScenarioQueryService { get; init; } = null!;

    [Inject] private ISetFilterDateRangeSucceededNotifier SetFilterDateRangeSucceededNotifier { get; init; } = null!;

    [Inject] private ISettingsService Settings { get; init; } = null!;

    private IReadOnlyList<ScenarioDefinition> VisibleScenarioMatches =>
        EffectiveScenarioGroup is { } group
            ? [.. ScenarioMatchGroups.First(match => match.Key == group)]
            : [];

    internal static IReadOnlyList<string> AvailableTagsForSets(IReadOnlyList<LibraryEntryFilterSet> sets) =>
        [.. sets.SelectMany(s => s.Tags).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.OrdinalIgnoreCase)];

    internal static IReadOnlyList<LibraryEntryFilterSet> FilterSetsByTags(
        IReadOnlyList<LibraryEntryFilterSet> sets,
        IReadOnlyList<string> selectedTags,
        LibraryEntryId currentSelection)
    {
        var available = AvailableTagsForSets(sets);
        var effective = selectedTags.Where(t => available.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList();

        return
        [
            .. sets
                .Where(s => effective.Count == 0
                    || effective.All(t => s.Tags.Contains(t, StringComparer.OrdinalIgnoreCase))
                    || s.Id.Equals(currentSelection))
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
        ];
    }

    internal void ApplyFilterSetSelection()
    {
        if (!TryResolveSelectedFilterSet()) { return; }

        FilterLibraryCommands.ApplyEntry(SelectedFilterSetId);
        CancelFilterSetPicker();
    }

    internal void ApplyScenarioSelection()
    {
        if (ResolvedScenario is not { } scenario)
        {
            AnnouncementService.Announce(FilterPaneAnnouncements.SelectedScenarioMissing);
            return;
        }

        ApplyScenario(scenario, replace: false);
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

    internal void EditDateFilter() => _canEditDate = true;

    internal string? GetRecentDisabledReason()
    {
        if (HasRecentFilters) { return null; }

        if (LibraryLoadStatus.Current.LoadError) { return FilterPaneAnnouncements.LoadFailedRetryViaModal; }

        return !LibraryLoadStatus.Current.IsLoaded ?
            FilterPaneAnnouncements.LoadingTryAgain :
            FilterPaneAnnouncements.RecentNoneAvailable;
    }

    internal void OnScenarioGroupChanged(ScenarioGroup? group)
    {
        SelectedScenarioGroup = group;
        SelectedScenarioId = VisibleScenarioMatches.FirstOrDefault()?.Id;
    }

    internal void OpenFilterSetPicker()
    {
        if (IsFilterSetPickerVisible) { return; }

        if (LibraryLoadStatus.Current.LoadError)
        {
            AnnouncementService.Announce(FilterPaneAnnouncements.LoadFailedRetryViaModal);
            return;
        }

        if (!LibraryLoadStatus.Current.IsLoaded)
        {
            AnnouncementService.Announce(FilterPaneAnnouncements.LoadingTryAgain);
            return;
        }

        _filterSetTags.Clear();
        IsFilterSetPickerVisible = true;
        SelectedFilterSetId = HasFilterSets
            ? LibraryEntries.Current
                .OfType<LibraryEntryFilterSet>()
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .First().Id
            : default;
        _isFilterListVisible = true;
    }

    internal void OpenScenarioPicker()
    {
        if (IsScenarioPickerVisible) { return; }

        IsScenarioPickerVisible = true;
        SelectedScenarioGroup = ScenarioMatchGroups.FirstOrDefault()?.Key;
        SelectedScenarioId = VisibleScenarioMatches.FirstOrDefault()?.Id;
        _isFilterListVisible = true;
    }

    internal void ReplaceFilterSetSelection()
    {
        if (!TryResolveSelectedFilterSet()) { return; }

        FilterLibraryCommands.ReplaceWithEntry(SelectedFilterSetId);
        CancelFilterSetPicker();
    }

    internal void ReplaceScenarioSelection()
    {
        if (ResolvedScenario is not { } scenario)
        {
            AnnouncementService.Announce(FilterPaneAnnouncements.SelectedScenarioMissing);
            return;
        }

        ApplyScenario(scenario, replace: true);
    }

    internal void SetQuickDateRange(DateFilter dateFilter) => UpdateFilterDate(dateFilter);

    internal bool TryResolveSelectedFilterSet()
    {
        if (LibraryLoadStatus.Current.LoadError)
        {
            AnnouncementService.Announce(FilterPaneAnnouncements.LoadFailedRetryViaModal);
            CancelFilterSetPicker();

            return false;
        }

        if (!LibraryLoadStatus.Current.IsLoaded)
        {
            AnnouncementService.Announce(FilterPaneAnnouncements.LoadingTryAgain);
            CancelFilterSetPicker();

            return false;
        }

        var filterSets = LibraryEntries.Current.OfType<LibraryEntryFilterSet>().ToList();

        if (filterSets.Any(p => p.Id.Equals(SelectedFilterSetId)))
        {
            return true;
        }

        AnnouncementService.Announce(FilterPaneAnnouncements.SelectedFilterSetMissing);
        SelectedFilterSetId = filterSets
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()?.Id ?? default;

        return false;
    }

    protected override async ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;

            Settings.TimeZoneChanged -= UpdateFilterDateTimeZone;
            MenuService.StateChanged -= OnMenuServiceStateChanged;
        }

        await base.DisposeAsyncCore(disposing);

        if (disposing)
        {
            await JsModuleInterop.DisposeModuleSafelyAsync(
                _scrollSuppressorModule,
                module => module.InvokeVoidAsync("release", _filterPaneRootRef));

            await JsModuleInterop.DisposeModuleSafelyAsync(_menuAnchorModule);

            _menuAnchorModule = null;
            _scrollSuppressorModule = null;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                _scrollSuppressorModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
                    "import",
                    "./_content/EventLogExpert.UI/Common/keyboardScrollSuppressor.js");

                await _scrollSuppressorModule.InvokeVoidAsync(
                    "suppress",
                    _filterPaneRootRef,
                    new[]
                    {
                        new { selector = ".split-button-chevron", keys = new[] { "ArrowUp", "ArrowDown" } }
                    });
            }
            catch (JSDisconnectedException) { }
            catch (JSException) { }
        }

        PruneStaleRowRefs();
        PruneStaleFilterSetTags();

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

            if (_addFilterButton is { } addFilterButton)
            {
                await ElementFocus.SafelyAsync(addFilterButton.Element);
            }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override void OnInitialized()
    {
        _editContext = new EditContext(_model);

        ObserveSource(LibraryEntries);
        ObserveSource(LibraryLoadStatus);
        ObserveSource(ActiveFilters);
        ObserveSource(FilteredDateRange);
        ObserveSource(OpenLogsPresence);
        ObserveSource(LoadedLogNames);

        ObserveSource(
            handler => ClearAllFiltersNotifier.Requested += handler,
            handler => ClearAllFiltersNotifier.Requested -= handler,
            () =>
            {
                if (_disposed) { return Task.CompletedTask; }

                _canEditDate = false;
                _pendingDrafts.Clear();
                IsFilterSetPickerVisible = false;
                CancelScenarioPicker();
                SelectedFilterSetId = default;
                _filterSetTags.Clear();
                StateHasChanged();

                return Task.CompletedTask;
            });

        ObserveSource(
            handler => SetFilterDateRangeSucceededNotifier.Succeeded += handler,
            handler => SetFilterDateRangeSucceededNotifier.Succeeded -= handler,
            () =>
            {
                if (_disposed) { return Task.CompletedTask; }

                UpdateFilterDate(FilteredDateRange.Current);
                StateHasChanged();

                return Task.CompletedTask;
            });

        Settings.TimeZoneChanged += UpdateFilterDateTimeZone;
        MenuService.StateChanged += OnMenuServiceStateChanged;

        _clipboardExporter = new ScenarioClipboardExporter(AnnouncementService, AlertDialogService, ClipboardService);

        _authoringContext = ScenarioAuthoringOptions.Enabled
            ? new ScenarioAuthoringRowContext(Enabled: true, CopyActiveRowAsync)
            : null;

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

        var (after, before) = EventLogQueries.GetEventDateRange(DateTime.UtcNow);

        _model = _model with
        {
            After = after.ConvertTimeZone(_currentTimeZone),
            Before = before.ConvertTimeZone(_currentTimeZone)
        };

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

    private void ApplyScenario(ScenarioDefinition scenario, bool replace)
    {
        ScenarioApplyService.ApplyInApp(scenario, replace);
        CancelScenarioPicker();
    }

    private void CancelFilterSetPicker()
    {
        IsFilterSetPickerVisible = false;
        SelectedFilterSetId = default;
        _filterSetTags.Clear();
    }

    private void CancelScenarioPicker()
    {
        IsScenarioPickerVisible = false;
        SelectedScenarioGroup = null;
        SelectedScenarioId = null;
        _scenarioMatchSource = null;
        _scenarioMatchGroups = [];
    }

    private async Task ClearAllFiltersAsync()
    {
        if (!HasClearableFilters) { return; }

        int count = ActiveFilters.Current.Count +
            _pendingDrafts.Count +
            (IsDateFilterVisible ? 1 : 0);

        string message = count == 1 ?
            "Clear 1 filter? This cannot be undone." :
            $"Clear {count} filters? This cannot be undone.";

        bool confirmed = await AlertDialogService.ShowAlert("Clear All Filters", message, "Clear", "Cancel");

        if (confirmed) { FilterPaneCommands.ClearAllFilters(); }
    }

    private Task CopyActiveRowAsync(SavedFilter filter) =>
        _clipboardExporter.CopyAsync(
            ScenarioAuthoringService.ExportRows([filter], CurrentChannelNames()),
            "Filter copied to the clipboard as scenario JSON.",
            "this filter");

    private Task CopyScenarioJsonAsync() =>
        _clipboardExporter.CopyAsync(ExportCurrentRows(), "Scenario JSON copied to the clipboard.", "these filters");

    private IReadOnlyList<string> CurrentChannelNames() => EventLogQueries.GetChannelNames();

    private ScenarioExportResult ExportCurrentRows() =>
        ScenarioAuthoringService.ExportRows(
            [.. ActiveFilters.Current.Where(filter => filter.IsEnabled)],
            CurrentChannelNames());

    private int GetActiveFilters()
    {
        int count = 0;

        count += FilteredDateRange.Current?.IsEnabled is true ? 1 : 0;
        count += ActiveFilters.Current.Count(filter => filter.IsEnabled);

        return count;
    }

    private string GetFilterSetName(LibraryEntryId id)
    {
        var set = LibraryEntries.Current.OfType<LibraryEntryFilterSet>().FirstOrDefault(p => p.Id.Equals(id));

        return set is null ? string.Empty : FormatFilterSetLabel(set);
    }

    private string GetScenarioName(string? id)
    {
        var scenario = ScenarioMatches.FirstOrDefault(match => match.Id == id);

        return scenario is null ? string.Empty : scenario.Name;
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

    private void HandlePendingDiscard(FilterDraft draft)
    {
        _pendingDrafts.Remove(draft);

        var target = FilterPaneFocus.ComputeFocusTargetAfterPendingDiscard(
            ActiveFilters.Current,
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
            ActiveFilters.Current,
            removedId,
            IsFocusable);

        _focusTargetAfterRemove = target;
        _focusAddButtonAfterRemove = target is null;
    }

    private bool IsFocusable(FilterId id) =>
        _rowRefs.TryGetValue(id, out var row) && row is not null && !row.IsEditing;

    private void OnFilterSetTagsChanged(List<string> tags)
    {
        if (ReferenceEquals(tags, _filterSetTags)) { return; }

        _filterSetTags.Clear();
        _filterSetTags.AddRange(tags);
    }

    private void OnMenuServiceStateChanged() => RequestGuardedRender(StateHasChanged);

    private void OnRowDisposed(FilterId id) => _rowRefs.Remove(id);

    private async Task OpenAddFilterMenuAsync() => await OpenAddFilterMenuAtAsync(true);

    private async Task OpenAddFilterMenuAtAsync(bool focusFirst)
    {
        if (_addFilterChevron is not { } chevronButton) { return; }

        _menuAnchorModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>(
            "import", "./_content/EventLogExpert.UI/Menu/MenuAnchor.js");

        var rect = await _menuAnchorModule.InvokeAsync<MenuAnchorRect>("getMenuElementRect", chevronButton.Element);
        MenuService.OpenAt(rect.Left, rect.Bottom, BuildAddFilterMenu(), focusFirst);
        _addFilterMenuId = MenuService.ActiveMenuId;
        StateHasChanged();
    }

    private Task OpenFilterLibraryAsync() => ModalCoordinator.OpenFilterLibraryAsync();

    private void PruneStaleFilterSetTags()
    {
        if (_filterSetTags.Count == 0) { return; }

        var available = AvailableFilterSetTags;

        if (_filterSetTags.RemoveAll(t => !available.Contains(t, StringComparer.OrdinalIgnoreCase)) > 0)
        {
            StateHasChanged();
        }
    }

    private void PruneStaleRowRefs()
    {
        if (_rowRefs.Count == 0) { return; }

        var liveFilters = ActiveFilters.Current;

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

    private async Task SaveScenarioJsonAsync()
    {
        var export = ExportCurrentRows();

        if (_clipboardExporter.NotExportable(export, "these filters")) { return; }

        var path = await FilePickerService.PickSaveAsync("Export scenario JSON", [".json"], "scenario.json");

        if (path is null) { return; }

        try
        {
            await File.WriteAllTextAsync(path, export.Json);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            await AlertDialogService.ShowAlert("Export failed", exception.Message, "OK");

            return;
        }

        await _clipboardExporter.AnnounceAsync($"Scenario JSON saved to {path}.", export.Warnings);
    }

    private void ToggleDateFilter() => FilterPaneCommands.ToggleFilterDate();

    private void ToggleMenu() => _isFilterListVisible = !_isFilterListVisible;

    private void UpdateFilterDate(DateFilter? updatedDate)
    {
        _model = _model with
        {
            Before = updatedDate?.Before?.ConvertTimeZone(_currentTimeZone),
            After = updatedDate?.After?.ConvertTimeZone(_currentTimeZone)
        };
    }

    private void UpdateFilterDateTimeZone(object? sender, TimeZoneInfo timeZoneInfo)
    {
        if (_disposed) { return; }

        _model = _model with
        {
            Before = _model.Before is not null ?
                TimeZoneInfo.ConvertTime(_model.Before.Value, _currentTimeZone, timeZoneInfo) : null,
            After = _model.After is not null ?
                TimeZoneInfo.ConvertTime(_model.After.Value, _currentTimeZone, timeZoneInfo) : null
        };

        _currentTimeZone = timeZoneInfo;
    }
}
