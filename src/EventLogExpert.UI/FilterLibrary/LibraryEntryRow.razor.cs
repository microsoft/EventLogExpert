// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.Common.Interop;
using EventLogExpert.UI.Focus;
using EventLogExpert.UI.Inputs;
using EventLogExpert.UI.Menu;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
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
    private Button? _moreMenuButton;
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

    [Inject] private IActiveFiltersSource ActiveFilters { get; init; } = null!;

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IAnnouncementService AnnouncementService { get; init; } = null!;

    private string FavoriteAriaLabel => Entry.IsFavorite ?
        Localizer["FilterLibrary_Entry_RemoveFavoriteAria", Entry.Name] :
        Localizer["FilterLibrary_Entry_AddFavoriteAria", Entry.Name];

    private string FavoriteIconClass => Entry.IsFavorite ? "bi bi-star-fill" : "bi bi-star";

    private string FavoriteTitle => Entry.IsFavorite ?
        Localizer["FilterLibrary_Entry_RemoveFavoriteTitle"] :
        Localizer["FilterLibrary_Entry_AddFavoriteTitle"];

    [Inject] private IFilterLibraryCommands FilterLibraryCommands { get; init; } = null!;

    private bool IsFavoritable => Entry is LibraryEntrySavedFilter;

    private bool IsMoreMenuOpen =>
        _moreMenuId != 0 && MenuService.ActiveMenuId == _moreMenuId && MenuService.ActiveItems is not null;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    private string KindAriaLabel => Entry is LibraryEntryFilterSet ?
        Localizer["FilterLibrary_Entry_FilterSetKindAria"] :
        Localizer["FilterLibrary_Entry_FilterKindAria"];

    private string KindIconClass =>
        Entry is LibraryEntryFilterSet ?
            "bi bi-collection library-entry-kind-icon" :
            "bi bi-funnel library-entry-kind-icon";

    [Inject] private ILibraryEntriesSource LibraryEntries { get; init; } = null!;

    [Inject] private IStringLocalizer<SharedResource> Localizer { get; init; } = null!;

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

    internal ValueTask<bool> FocusMoreActionsButtonAsync() =>
        _moreMenuButton is { } button ?
            ElementFocus.TrySafelyAsync(button.Element) :
            ValueTask.FromResult(false);

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

    private MenuItem BuildAddToFilterSetItem(LibraryEntrySavedFilter filterEntry)
    {
        var children = new List<MenuItem>
        {
            MenuItem.Item(Localizer["FilterLibrary_Entry_NewFilterSetMenu"], () => OnNewFilterSetSelectedAsync(filterEntry)),
        };

        if (AllFilterSets.Count <= 0)
        {
            return MenuItem.SubMenu(Localizer["FilterLibrary_Entry_AddToFilterSetMenu"], children);
        }

        children.Add(MenuItem.Separator());

        foreach (var filterSet in AllFilterSets)
        {
            var pid = filterSet.Id;
            var pname = filterSet.Name;
            children.Add(MenuItem.Item(pname, () => OnExistingFilterSetSelectedAsync(filterEntry, pid, pname)));
        }

        return MenuItem.SubMenu(Localizer["FilterLibrary_Entry_AddToFilterSetMenu"], children);
    }

    private IReadOnlyList<MenuItem> BuildMoreMenu()
    {
        var items = new List<MenuItem>
        {
            MenuItem.Item(Localizer["FilterLibrary_Entry_ReplaceCurrentMenu"], OnReplaceAsync),
        };

        if (ShowSaveToLibraryItem)
        {
            items.Add(MenuItem.Item(Localizer["FilterLibrary_Entry_SaveToLibraryMenu"], OnSaveToLibraryAsync));
        }

        if (Entry is LibraryEntrySavedFilter filterEntry)
        {
            items.Add(BuildAddToFilterSetItem(filterEntry));
        }

        if (Entry.Origin == LibraryEntryOrigin.UserSaved)
        {
            items.Add(MenuItem.Item(Localizer["FilterLibrary_Entry_RenameMenu"], OnRenameAsync));
        }

        if (Entry is LibraryEntryFilterSet && OnExportEntry.HasDelegate)
        {
            items.Add(MenuItem.Item(Localizer["FilterLibrary_Entry_ExportMenu"], OnExportEntryAsync));
        }

        if (Entry is LibraryEntryFilterSet && OnCopyScenario.HasDelegate)
        {
            items.Add(MenuItem.Item(Localizer["FilterEditor_RowAction_CopyScenarioTitle"], OnCopyScenarioAsync));
        }

        if (Entry is LibraryEntryFilterSet && OnSaveScenario.HasDelegate)
        {
            items.Add(MenuItem.Item(Localizer["FilterLibrary_Entry_SaveScenarioMenu"], OnSaveScenarioAsync));
        }

        items.Add(MenuItem.Separator());
        items.Add(MenuItem.Item(Localizer["FilterLibrary_Entry_DeleteMenu"], OnDeleteAsync, isDanger: true));

        return items;
    }

    private string FormatRelativeTime(DateTimeOffset lastUsed)
    {
        var diff = DateTimeOffset.UtcNow - lastUsed;

        if (diff.TotalSeconds < 60) { return Localizer["FilterLibrary_Entry_RelativeTime_JustNow"]; }

        if (diff.TotalMinutes < 60) { return Localizer["FilterLibrary_Entry_RelativeTime_Minutes", (int)diff.TotalMinutes]; }

        if (diff.TotalHours < 24) { return Localizer["FilterLibrary_Entry_RelativeTime_Hours", (int)diff.TotalHours]; }

        return diff.TotalDays < 7 ?
            Localizer["FilterLibrary_Entry_RelativeTime_Days", (int)diff.TotalDays] :
            lastUsed.ToLocalTime().ToString("yyyy-MM-dd");
    }

    private bool HasDuplicateNameOfSameKind(string candidateName)
    {
        return LibraryEntries.Current.Any(other =>
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
            var message = Entry is LibraryEntryFilterSet filterSet ?
                LocalizedCount.OneOrManyRaw(
                    Localizer,
                    filterSet.Filters.Count,
                    "FilterLibrary_Entry_DeleteFilterSetMessage_One",
                    "FilterLibrary_Entry_DeleteFilterSetMessage_Many",
                    Entry.Name,
                    filterSet.Filters.Count) :
                Localizer["FilterLibrary_Entry_DeleteFilterMessage", Entry.Name];

            var confirmed = await AlertDialogService.ShowAlert(
                Localizer["FilterLibrary_Entry_DeleteTitle"],
                message,
                Localizer["FilterLibrary_Entry_DeleteMenu"],
                Localizer["Modal_Cancel"]);

            if (!confirmed) { return; }
        }

        await OnRequestPendingFocus.InvokeAsync(Entry.Id);
        await OnDelete.InvokeAsync(Entry.Id);

        AnnouncementService.Announce(Localizer["FilterLibrary_Entry_DeletedAnnouncement", Entry.Name]);
    }

    private async Task OnExistingFilterSetSelectedAsync(LibraryEntrySavedFilter filterEntry, LibraryEntryId filterSetId, string filterSetName)
    {
        await OnAddToFilterSet.InvokeAsync(new AddToFilterSetIntent(filterEntry.Filter, filterSetId, null, filterEntry.Id));

        AnnouncementService.Announce(Localizer["FilterLibrary_Entry_AddedToFilterSetAnnouncement", filterSetName]);
    }

    private Task OnExportEntryAsync() => OnExportEntry.InvokeAsync(Entry.Id);

    private void OnMenuServiceStateChanged() => _ = InvokeAsync(StateHasChanged);

    private async Task OnNewFilterSetSelectedAsync(LibraryEntrySavedFilter filterEntry)
    {
        var name = await AlertDialogService.DisplayPrompt(
            Localizer["FilterLibrary_Entry_NewFilterSetPromptTitle"],
            Localizer["FilterLibrary_Entry_NewFilterSetPromptMessage"],
            Localizer["FilterLibrary_Entry_NewFilterSetPromptInitialValue"],
            candidate =>
            {
                var trimmed = candidate?.Trim() ?? string.Empty;

                if (string.IsNullOrEmpty(trimmed)) { return Localizer["FilterLibrary_Entry_NameEmptyValidation"].Value; }

                return AllFilterSets.Any(fs => string.Equals(fs.Name, trimmed, StringComparison.OrdinalIgnoreCase)) ?
                    Localizer["FilterLibrary_Entry_FilterSetExistsValidation", trimmed].Value : null;
            });

        _pendingFocusMoreButton = true;

        if (string.IsNullOrWhiteSpace(name)) { return; }

        await OnAddToFilterSet.InvokeAsync(new AddToFilterSetIntent(filterEntry.Filter, null, name, filterEntry.Id));
        AnnouncementService.Announce(Localizer["FilterLibrary_Entry_AddedToNewFilterSetAnnouncement", name]);
    }

    private Task OnRemoveTagAsync(string tag)
    {
        var newTags = Entry.Tags.RemoveAll(t => string.Equals(t, tag, StringComparison.Ordinal));

        if (newTags.Count == Entry.Tags.Count) { return Task.CompletedTask; }

        FilterLibraryCommands.SetEntryTags(Entry.Id, newTags);
        AnnouncementService.Announce(Localizer["FilterLibrary_Entry_TagRemovedAnnouncement", tag, Entry.Name]);

        return Task.CompletedTask;
    }

    private async Task OnRenameAsync()
    {
        var newName = await AlertDialogService.DisplayPrompt(
            Localizer["FilterLibrary_Entry_RenamePromptTitle"],
            Localizer["FilterLibrary_Entry_RenamePromptMessage"],
            Entry.Name,
            candidate =>
            {
                var trimmed = candidate?.Trim() ?? string.Empty;

                if (string.IsNullOrEmpty(trimmed)) { return Localizer["FilterLibrary_Entry_NameEmptyValidation"].Value; }

                if (string.Equals(trimmed, Entry.Name, StringComparison.Ordinal)) { return null; }

                return HasDuplicateNameOfSameKind(trimmed) ? Localizer["FilterLibrary_Entry_EntryExistsValidation", trimmed].Value : null;
            });

        _pendingFocusMoreButton = true;

        if (string.IsNullOrWhiteSpace(newName)) { return; }

        var trimmed = newName.Trim();

        if (string.Equals(trimmed, Entry.Name, StringComparison.Ordinal)) { return; }

        FilterLibraryCommands.SetEntryName(Entry.Id, trimmed);
        AnnouncementService.Announce(Localizer["FilterLibrary_Entry_RenamedAnnouncement", trimmed]);
    }

    private async Task OnReplaceAsync()
    {
        if (!ActiveFilters.Current.IsEmpty)
        {
            var confirmed = await AlertDialogService.ShowAlert(
                Localizer["FilterLibrary_Entry_ReplacePromptTitle"],
                Localizer["FilterLibrary_Entry_ReplacePromptMessage", Entry.Name],
                Localizer["FilterPane_Action_Replace"],
                Localizer["Modal_Cancel"]);

            if (!confirmed) { return; }
        }

        await OnReplace.InvokeAsync(Entry.Id);
    }

    private Task OnSaveScenarioAsync() => OnSaveScenario.InvokeAsync(Entry.Id);

    private async Task OnSaveToLibraryAsync()
    {
        await OnSaveToLibrary.InvokeAsync(Entry.Id);
        AnnouncementService.Announce(Localizer["FilterLibrary_Entry_SavedToLibraryAnnouncement", Entry.Name]);
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

        AnnouncementService.Announce(newIsFavorite ?
            Localizer["FilterLibrary_Entry_MarkedFavoriteAnnouncement", Entry.Name] :
            Localizer["FilterLibrary_Entry_RemovedFavoriteAnnouncement", Entry.Name]);
    }

    private void ToggleEditTagsMode()
    {
        _isEditingTags = !_isEditingTags;

        if (_isEditingTags) { _pendingScrollEditIntoView = true; }
    }

    private void ToggleExpand() => _isExpanded = !_isExpanded;

    private async Task ToggleMoreMenuAsync(MouseEventArgs args)
    {
        if (IsMoreMenuOpen) { MenuService.Close(); return; }

        if (_moreMenuButton is not { } moreMenuButton) { return; }

        try
        {
            _menuAnchorModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>(
                "import", "./_content/EventLogExpert.UI/Menu/MenuAnchor.js");

            var rect = await _menuAnchorModule.InvokeAsync<MenuAnchorRect>("getMenuElementRect", moreMenuButton.Element);
            MenuService.OpenAt(rect.Left, rect.Bottom, BuildMoreMenu(), focusFirst: true,
                openedByKeyboard: MenuButtonActivation.WasKeyboardTriggered(args));
            _moreMenuId = MenuService.ActiveMenuId;
            StateHasChanged();
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
    }
}
