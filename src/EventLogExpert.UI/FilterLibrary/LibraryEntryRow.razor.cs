// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.UI.Focus;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EventLogExpert.UI.FilterLibrary;

public sealed partial class LibraryEntryRow : ComponentBase, IAsyncDisposable
{
    private ElementReference _moreMenuButtonRef;
    private long _moreMenuId;

    [Parameter][EditorRequired] public LibraryTab ActiveTab { get; set; }

    [Parameter][EditorRequired] public required IReadOnlyList<LibraryEntryPreset> AllPresets { get; set; }

    [Parameter][EditorRequired] public required LibraryEntry Entry { get; set; }

    [Parameter][EditorRequired] public EventCallback<AddToPresetIntent> OnAddToPreset { get; set; }

    [Parameter][EditorRequired] public EventCallback<LibraryEntryId> OnApply { get; set; }

    [Parameter][EditorRequired] public EventCallback<LibraryEntryId> OnDelete { get; set; }

    [Parameter][EditorRequired] public EventCallback<LibraryEntryId> OnReplace { get; set; }

    [Parameter][EditorRequired] public EventCallback<LibraryEntryId> OnRequestPendingFocus { get; set; }

    [Parameter][EditorRequired] public EventCallback<LibraryEntryId> OnSaveToLibrary { get; set; }

    [Parameter][EditorRequired] public EventCallback<FavoriteToggleIntent> OnToggleFavorite { get; set; }

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IAnnouncementService AnnouncementService { get; init; } = null!;

    private string FavoriteAriaLabel => Entry.IsFavorite
        ? $"Remove {Entry.Name} from favorites"
        : $"Add {Entry.Name} to favorites";

    private string FavoriteIconClass => Entry.IsFavorite ? "bi bi-star-fill" : "bi bi-star";

    private string FavoriteTitle => Entry.IsFavorite ? "Remove from favorites" : "Add to favorites";

    [Inject] private IState<FilterPaneState> FilterPaneState { get; init; } = null!;

    private bool IsMoreMenuOpen =>
        _moreMenuId != 0 && MenuService.ActiveMenuId == _moreMenuId && MenuService.ActiveItems is not null;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    private string KindAriaLabel => Entry is LibraryEntryPreset ? "Preset" : "Filter";

    private string KindIconClass => Entry is LibraryEntryPreset
        ? "bi bi-collection library-entry-kind-icon"
        : "bi bi-funnel library-entry-kind-icon";

    [Inject] private IMenuService MenuService { get; init; } = null!;

    private bool ShowSaveToLibraryItem =>
        Entry is { Origin: LibraryEntryOrigin.AutoTracked, IsFavorite: false };

    private string StatusBadgeKind
    {
        get
        {
            if (Entry.IsFavorite) { return "favorite"; }

            return Entry is { Origin: LibraryEntryOrigin.AutoTracked, LastUsedUtc: not null } ? "previously-used" : "saved";
        }
    }

    private string StatusBadgeText
    {
        get
        {
            if (Entry.IsFavorite) { return "Favorite"; }

            return Entry is { Origin: LibraryEntryOrigin.AutoTracked, LastUsedUtc: not null } ? "Previously used" : "Saved";
        }
    }

    public ValueTask DisposeAsync()
    {
        MenuService.StateChanged -= OnMenuServiceStateChanged;

        return ValueTask.CompletedTask;
    }

    internal ValueTask<bool> FocusMoreActionsButtonAsync() => ElementFocus.TrySafelyAsync(_moreMenuButtonRef);

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

    private MenuItem BuildAddToPresetItem(LibraryEntrySavedFilter filterEntry)
    {
        var children = new List<MenuItem>
        {
            MenuItem.Item("+ New preset...", () => OnNewPresetSelectedAsync(filterEntry)),
        };

        if (AllPresets.Count <= 0)
        {
            return MenuItem.SubMenu("Add to preset...", children);
        }

        children.Add(MenuItem.Separator());

        foreach (var preset in AllPresets)
        {
            var pid = preset.Id;
            var pname = preset.Name;
            children.Add(MenuItem.Item(pname, () => OnExistingPresetSelectedAsync(filterEntry, pid, pname)));
        }

        return MenuItem.SubMenu("Add to preset...", children);
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
            items.Add(BuildAddToPresetItem(filterEntry));
        }

        items.Add(MenuItem.Separator());
        items.Add(MenuItem.Item("Delete", OnDeleteAsync, isDanger: true));

        return items;
    }

    private Task OnApplyAsync() => OnApply.InvokeAsync(Entry.Id);

    private async Task OnDeleteAsync()
    {
        var needsConfirm = Entry is LibraryEntryPreset || Entry.Origin == LibraryEntryOrigin.UserSaved;

        if (needsConfirm)
        {
            var entryKind = Entry is LibraryEntryPreset p
                ? $"preset '{Entry.Name}' with {p.Filters.Count} filter{(p.Filters.Count == 1 ? "" : "s")}"
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

    private async Task OnExistingPresetSelectedAsync(LibraryEntrySavedFilter filterEntry, LibraryEntryId presetId, string presetName)
    {
        await OnAddToPreset.InvokeAsync(new AddToPresetIntent(filterEntry.Filter, presetId, null, filterEntry.Id));
        AnnouncementService.Announce($"Added filter to preset '{presetName}'");
    }

    private void OnMenuServiceStateChanged() => _ = InvokeAsync(StateHasChanged);

    private async Task OnNewPresetSelectedAsync(LibraryEntrySavedFilter filterEntry)
    {
        var name = await AlertDialogService.DisplayPrompt(
            "Preset name",
            "What would you like to name this preset? (use \\ for folder paths)",
            "New Preset");

        if (string.IsNullOrWhiteSpace(name)) { return; }

        await OnAddToPreset.InvokeAsync(new AddToPresetIntent(filterEntry.Filter, null, name, filterEntry.Id));
        AnnouncementService.Announce($"Added filter to new preset '{name}'");
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

    private async Task OnSaveToLibraryAsync()
    {
        await OnSaveToLibrary.InvokeAsync(Entry.Id);
        AnnouncementService.Announce($"Saved {Entry.Name} to library");
    }

    private async Task OnToggleFavoriteAsync()
    {
        var newIsFavorite = !Entry.IsFavorite;
        var willLeaveActiveTab =
            (ActiveTab == LibraryTab.Favorites && Entry.IsFavorite) ||
            (ActiveTab == LibraryTab.PreviouslyUsed && newIsFavorite);

        if (willLeaveActiveTab) { await OnRequestPendingFocus.InvokeAsync(Entry.Id); }

        await OnToggleFavorite.InvokeAsync(new FavoriteToggleIntent(Entry.Id, newIsFavorite));
        AnnouncementService.Announce(newIsFavorite
            ? $"Marked {Entry.Name} as favorite"
            : $"Removed {Entry.Name} from favorites");
    }

    private async Task ToggleMoreMenuAsync()
    {
        if (IsMoreMenuOpen) { MenuService.Close(); return; }

        try
        {
            var rect = await JSRuntime.InvokeAsync<MenuAnchorRect>("getMenuElementRect", _moreMenuButtonRef);
            MenuService.OpenAt(rect.Left, rect.Bottom, BuildMoreMenu(), focusFirst: true);
            _moreMenuId = MenuService.ActiveMenuId;
            StateHasChanged();
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
    }
}
