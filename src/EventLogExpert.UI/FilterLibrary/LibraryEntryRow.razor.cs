// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.Common.Interop;
using EventLogExpert.UI.Focus;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Collections.Immutable;

namespace EventLogExpert.UI.FilterLibrary;

public sealed partial class LibraryEntryRow : ComponentBase, IAsyncDisposable
{
    private const int InlineTagChipCap = 2;
    private const int MaxVisibleTagChips = 5;

    private readonly string _filterEditorRegionId = ComponentId.NewUnique("lefr").Value;

    private ElementReference _articleRef;
    private bool _isEditingTags;
    private bool _isExpanded;
    private IJSObjectReference? _menuAnchorModule;
    private ElementReference _moreMenuButtonRef;
    private long _moreMenuId;
    private bool _pendingFocusMoreButton;
    private bool _pendingScrollEditIntoView;
    private IJSObjectReference? _rowModule;

    [Parameter][EditorRequired] public LibraryTab ActiveTab { get; set; }

    [Parameter][EditorRequired] public required IReadOnlyList<LibraryEntryFilterSet> AllFilterSets { get; set; }

    [Parameter] public IReadOnlyList<string> AllLibraryTags { get; set; } = [];

    [Parameter][EditorRequired] public required LibraryEntry Entry { get; set; }

    [Parameter][EditorRequired] public EventCallback<AddToFilterSetIntent> OnAddToFilterSet { get; set; }

    [Parameter][EditorRequired] public EventCallback<LibraryEntryId> OnApply { get; set; }

    [Parameter] public EventCallback<LibraryEntryId> OnCopyScenario { get; set; }

    [Parameter][EditorRequired] public EventCallback<LibraryEntryId> OnDelete { get; set; }

    [Parameter] public Action<(LibraryTab Tab, LibraryEntryId Id)>? OnDisposed { get; set; }

    [Parameter] public EventCallback<LibraryEntryId> OnExportEntry { get; set; }

    [Parameter][EditorRequired] public EventCallback<LibraryEntryId> OnReplace { get; set; }

    [Parameter][EditorRequired] public EventCallback<LibraryEntryId> OnRequestPendingFocus { get; set; }

    [Parameter] public EventCallback<LibraryEntryId> OnSaveScenario { get; set; }

    [Parameter][EditorRequired] public EventCallback<LibraryEntryId> OnSaveToLibrary { get; set; }

    [Parameter][EditorRequired] public EventCallback<FavoriteToggleIntent> OnToggleFavorite { get; set; }

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IAnnouncementService AnnouncementService { get; init; } = null!;

    private string FavoriteAriaLabel => Entry.IsFavorite
        ? $"Remove {Entry.Name} from favorites"
        : $"Add {Entry.Name} to favorites";

    private string FavoriteIconClass => Entry.IsFavorite ? "bi bi-star-fill" : "bi bi-star";

    private string FavoriteTitle => Entry.IsFavorite ? "Remove from favorites" : "Add to favorites";

    [Inject] private IFilterLibraryCommands FilterLibraryCommands { get; init; } = null!;

    [Inject] private IState<FilterLibraryState> FilterLibraryState { get; init; } = null!;

    [Inject] private IState<FilterPaneState> FilterPaneState { get; init; } = null!;

    private bool IsFavoritable => Entry is LibraryEntrySavedFilter;

    private bool IsMoreMenuOpen =>
        _moreMenuId != 0 && MenuService.ActiveMenuId == _moreMenuId && MenuService.ActiveItems is not null;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    private string KindAriaLabel => Entry is LibraryEntryFilterSet ? "Filter set" : "Filter";

    private string KindIconClass => Entry is LibraryEntryFilterSet
        ? "bi bi-collection library-entry-kind-icon"
        : "bi bi-funnel library-entry-kind-icon";

    [Inject] private IMenuService MenuService { get; init; } = null!;

    private bool ShowSaveToLibraryItem =>
        Entry is { Origin: LibraryEntryOrigin.AutoTracked, IsFavorite: false };

    public async ValueTask DisposeAsync()
    {
        MenuService.StateChanged -= OnMenuServiceStateChanged;
        OnDisposed?.Invoke((ActiveTab, Entry.Id));

        await JsModuleInterop.DisposeModuleSafelyAsync(_rowModule);

        _rowModule = null;

        await JsModuleInterop.DisposeModuleSafelyAsync(_menuAnchorModule);

        _menuAnchorModule = null;
    }

    internal ValueTask<bool> FocusMoreActionsButtonAsync() => ElementFocus.TrySafelyAsync(_moreMenuButtonRef);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_pendingFocusMoreButton)
        {
            _pendingFocusMoreButton = false;
            await FocusMoreActionsButtonAsync();
        }

        if (_pendingScrollEditIntoView)
        {
            _pendingScrollEditIntoView = false;

            try
            {
                _rowModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>(
                    "import", "./_content/EventLogExpert.UI/FilterLibrary/LibraryEntryRow.razor.js");

                await _rowModule.InvokeVoidAsync("scrollElementIntoView", _articleRef);
            }
            catch (JSDisconnectedException) { }
            catch (JSException) { }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override void OnInitialized()
    {
        MenuService.StateChanged += OnMenuServiceStateChanged;

        base.OnInitialized();
    }

    private static string FormatRelativeTime(DateTimeOffset lastUsed)
    {
        var diff = DateTimeOffset.UtcNow - lastUsed;

        if (diff.TotalSeconds < 60) { return "just now"; }

        if (diff.TotalMinutes < 60) { return $"{(int)diff.TotalMinutes}m ago"; }

        if (diff.TotalHours < 24) { return $"{(int)diff.TotalHours}h ago"; }

        return diff.TotalDays < 7 ?
            $"{(int)diff.TotalDays}d ago" :
            lastUsed.ToLocalTime().ToString("yyyy-MM-dd");
    }

    private MenuItem BuildAddToFilterSetItem(LibraryEntrySavedFilter filterEntry)
    {
        var children = new List<MenuItem>
        {
            MenuItem.Item("+ New filter set...", () => OnNewFilterSetSelectedAsync(filterEntry)),
        };

        if (AllFilterSets.Count <= 0)
        {
            return MenuItem.SubMenu("Add to filter set...", children);
        }

        children.Add(MenuItem.Separator());

        foreach (var filterSet in AllFilterSets)
        {
            var pid = filterSet.Id;
            var pname = filterSet.Name;
            children.Add(MenuItem.Item(pname, () => OnExistingFilterSetSelectedAsync(filterEntry, pid, pname)));
        }

        return MenuItem.SubMenu("Add to filter set...", children);
    }

    private IReadOnlyList<MenuItem> BuildMoreMenu()
    {
        var items = new List<MenuItem>
        {
            MenuItem.Item("Replace current filters", OnReplaceAsync),
        };

        if (ShowSaveToLibraryItem)
        {
            items.Add(MenuItem.Item("Save to Library", OnSaveToLibraryAsync));
        }

        if (Entry is LibraryEntrySavedFilter filterEntry)
        {
            items.Add(BuildAddToFilterSetItem(filterEntry));
        }

        if (Entry.Origin == LibraryEntryOrigin.UserSaved)
        {
            items.Add(MenuItem.Item("Rename...", OnRenameAsync));
        }

        if (Entry is LibraryEntryFilterSet && OnExportEntry.HasDelegate)
        {
            items.Add(MenuItem.Item("Export...", OnExportEntryAsync));
        }

        if (Entry is LibraryEntryFilterSet && OnCopyScenario.HasDelegate)
        {
            items.Add(MenuItem.Item("Copy as scenario JSON", OnCopyScenarioAsync));
        }

        if (Entry is LibraryEntryFilterSet && OnSaveScenario.HasDelegate)
        {
            items.Add(MenuItem.Item("Save as scenario JSON", OnSaveScenarioAsync));
        }

        items.Add(MenuItem.Separator());
        items.Add(MenuItem.Item("Delete", OnDeleteAsync, isDanger: true));

        return items;
    }

    private bool HasDuplicateNameOfSameKind(string candidateName)
    {
        return FilterLibraryState.Value.Entries.Any(other =>
            !other.Id.Equals(Entry.Id) &&
            SameKind(other) &&
            string.Equals(other.Name, candidateName, StringComparison.OrdinalIgnoreCase));

        bool SameKind(LibraryEntry e) => Entry switch
        {
            LibraryEntryFilterSet => e is LibraryEntryFilterSet,
            LibraryEntrySavedFilter => e is LibraryEntrySavedFilter,
            _ => false,
        };
    }

    private Task OnApplyAsync() => OnApply.InvokeAsync(Entry.Id);

    private Task OnCopyScenarioAsync() => OnCopyScenario.InvokeAsync(Entry.Id);

    private async Task OnDeleteAsync()
    {
        var needsConfirm = Entry is LibraryEntryFilterSet || Entry.Origin == LibraryEntryOrigin.UserSaved;

        if (needsConfirm)
        {
            var entryKind = Entry is LibraryEntryFilterSet p
                ? $"filter set '{Entry.Name}' with {p.Filters.Count} filter{(p.Filters.Count == 1 ? "" : "s")}"
                : $"filter '{Entry.Name}'";

            var confirmed = await AlertDialogService.ShowAlert(
                "Delete from library?",
                $"This will permanently delete the {entryKind}. This cannot be undone.",
                "Delete",
                "Cancel");

            if (!confirmed) { return; }
        }

        await OnRequestPendingFocus.InvokeAsync(Entry.Id);
        await OnDelete.InvokeAsync(Entry.Id);
        AnnouncementService.Announce($"Deleted {Entry.Name} from library");
    }

    private async Task OnExistingFilterSetSelectedAsync(LibraryEntrySavedFilter filterEntry, LibraryEntryId filterSetId, string filterSetName)
    {
        await OnAddToFilterSet.InvokeAsync(new AddToFilterSetIntent(filterEntry.Filter, filterSetId, null, filterEntry.Id));
        AnnouncementService.Announce($"Added filter to filter set '{filterSetName}'");
    }

    private Task OnExportEntryAsync() => OnExportEntry.InvokeAsync(Entry.Id);

    private void OnMenuServiceStateChanged() => _ = InvokeAsync(StateHasChanged);

    private async Task OnNewFilterSetSelectedAsync(LibraryEntrySavedFilter filterEntry)
    {
        var name = await AlertDialogService.DisplayPrompt(
            "Filter set name",
            "What would you like to name this filter set?",
            "New Filter Set",
            candidate =>
            {
                var trimmed = candidate?.Trim() ?? string.Empty;

                if (string.IsNullOrEmpty(trimmed)) { return "Name cannot be empty."; }

                return AllFilterSets.Any(fs => string.Equals(fs.Name, trimmed, StringComparison.OrdinalIgnoreCase)) ?
                    $"A filter set named '{trimmed}' already exists." : null;
            });

        _pendingFocusMoreButton = true;

        if (string.IsNullOrWhiteSpace(name)) { return; }

        await OnAddToFilterSet.InvokeAsync(new AddToFilterSetIntent(filterEntry.Filter, null, name, filterEntry.Id));
        AnnouncementService.Announce($"Added filter to new filter set '{name}'");
    }

    private Task OnRemoveTagAsync(string tag)
    {
        var newTags = Entry.Tags.RemoveAll(t => string.Equals(t, tag, StringComparison.Ordinal));

        if (newTags.Count == Entry.Tags.Count) { return Task.CompletedTask; }

        FilterLibraryCommands.SetEntryTags(Entry.Id, newTags);
        AnnouncementService.Announce($"Removed tag '{tag}' from {Entry.Name}");

        return Task.CompletedTask;
    }

    private async Task OnRenameAsync()
    {
        var newName = await AlertDialogService.DisplayPrompt(
            "Rename entry",
            "What would you like to rename this entry to?",
            Entry.Name,
            candidate =>
            {
                var trimmed = candidate?.Trim() ?? string.Empty;

                if (string.IsNullOrEmpty(trimmed)) { return "Name cannot be empty."; }

                if (string.Equals(trimmed, Entry.Name, StringComparison.Ordinal)) { return null; }

                return HasDuplicateNameOfSameKind(trimmed) ? $"An entry named '{trimmed}' already exists." : null;
            });

        _pendingFocusMoreButton = true;

        if (string.IsNullOrWhiteSpace(newName)) { return; }

        var trimmed = newName.Trim();

        if (string.Equals(trimmed, Entry.Name, StringComparison.Ordinal)) { return; }

        FilterLibraryCommands.SetEntryName(Entry.Id, trimmed);
        AnnouncementService.Announce($"Renamed entry to '{trimmed}'");
    }

    private async Task OnReplaceAsync()
    {
        if (!FilterPaneState.Value.Filters.IsEmpty)
        {
            var confirmed = await AlertDialogService.ShowAlert(
                "Replace current filters?",
                $"This will replace your current filter list with the filters from '{Entry.Name}'. " +
                "Your date filter and any in-progress filter drafts will be kept.",
                "Replace",
                "Cancel");

            if (!confirmed) { return; }
        }

        await OnReplace.InvokeAsync(Entry.Id);
    }

    private Task OnSaveScenarioAsync() => OnSaveScenario.InvokeAsync(Entry.Id);

    private async Task OnSaveToLibraryAsync()
    {
        await OnSaveToLibrary.InvokeAsync(Entry.Id);
        AnnouncementService.Announce($"Saved {Entry.Name} to library");
    }

    private async Task OnTagsChangedAsync(ImmutableList<string> tags)
    {
        FilterLibraryCommands.SetEntryTags(Entry.Id, tags);
        await Task.CompletedTask;
    }

    private async Task OnToggleFavoriteAsync()
    {
        if (!IsFavoritable) { return; }

        var newIsFavorite = !Entry.IsFavorite;
        var willLeaveActiveTab =
            (ActiveTab == LibraryTab.Favorites && Entry.IsFavorite) ||
            (ActiveTab == LibraryTab.PreviouslyUsed && newIsFavorite) ||
            (ActiveTab == LibraryTab.Saved && newIsFavorite && Entry.Origin == LibraryEntryOrigin.UserSaved);

        if (willLeaveActiveTab) { await OnRequestPendingFocus.InvokeAsync(Entry.Id); }

        await OnToggleFavorite.InvokeAsync(new FavoriteToggleIntent(Entry.Id, newIsFavorite));
        AnnouncementService.Announce(newIsFavorite
            ? $"Marked {Entry.Name} as favorite"
            : $"Removed {Entry.Name} from favorites");
    }

    private void ToggleEditTagsMode()
    {
        _isEditingTags = !_isEditingTags;

        if (_isEditingTags) { _pendingScrollEditIntoView = true; }
    }

    private void ToggleExpand() => _isExpanded = !_isExpanded;

    private async Task ToggleMoreMenuAsync()
    {
        if (IsMoreMenuOpen) { MenuService.Close(); return; }

        try
        {
            _menuAnchorModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>(
                "import", "./_content/EventLogExpert.UI/Menu/MenuAnchor.js");

            var rect = await _menuAnchorModule.InvokeAsync<MenuAnchorRect>("getMenuElementRect", _moreMenuButtonRef);
            MenuService.OpenAt(rect.Left, rect.Bottom, BuildMoreMenu(), focusFirst: true);
            _moreMenuId = MenuService.ActiveMenuId;
            StateHasChanged();
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
    }
}
