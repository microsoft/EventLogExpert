// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.UI.Focus;
using EventLogExpert.UI.Modal;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace EventLogExpert.UI.FilterLibrary;

public sealed partial class FilterLibraryModal : ModalBase<bool>
{
    private static readonly (LibraryTab Tab, string Label)[] s_tabs =
    [
        (LibraryTab.Saved, "Saved"),
        (LibraryTab.Favorites, "Favorites"),
        (LibraryTab.PreviouslyUsed, "Previously Used"),
    ];
    private readonly Dictionary<LibraryEntryId, LibraryEntryRow?> _rowRefs = new();

    private readonly Dictionary<LibraryTab, ElementReference> _tabButtonRefs = new();

    private LibraryTab _activeTab = LibraryTab.Saved;
    private LibraryTab? _pendingFocusTab;
    private LibraryEntryId? _pendingFocusTargetEntryId;
    private bool _pendingFocusToActiveTab;
    private IJSObjectReference? _tabKeyModule;
    private bool _tabKeyShimAttached;
    private ElementReference _tablistRef;

    [Parameter] public LibraryTab? InitialTab { get; set; }

    private IReadOnlyList<LibraryEntryFilterSet> AllFilterSets =>
        [.. FilterLibraryState.Value.Entries
            .OfType<LibraryEntryFilterSet>()
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)];

    [Inject] private IAnnouncementService AnnouncementService { get; init; } = null!;

    private IReadOnlyList<LibraryEntry> CurrentTabEntries => _activeTab switch
    {
        LibraryTab.Favorites => FavoriteEntries,
        LibraryTab.PreviouslyUsed => PreviouslyUsedEntries,
        _ => SavedEntries,
    };

    private string EmptyStateMessage => _activeTab switch
    {
        LibraryTab.Favorites => "No favorited filters or filter sets yet. Star an entry to add it here.",
        LibraryTab.PreviouslyUsed => "No filters have been applied recently.",
        _ => "No saved filters or filter sets yet. Use \"Save as Filter Set\" from the filter pane.",
    };

    private IReadOnlyList<LibraryEntry> FavoriteEntries =>
        [.. FilterLibraryState.Value.Entries
            .Where(e => e.IsFavorite)
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)];

    [Inject] private IFilterLibraryCommands FilterLibraryCommands { get; init; } = null!;

    [Inject] private IState<FilterLibraryState> FilterLibraryState { get; init; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    private bool NeedsTabpanelFocus => CurrentTabEntries.Count == 0;

    private IReadOnlyList<LibraryEntry> PreviouslyUsedEntries =>
        [.. FilterLibraryState.Value.Entries
            .Where(e => e.LastUsedUtc is not null)
            .OrderByDescending(e => e.LastUsedUtc!.Value)
            .Take(50)];

    private IReadOnlyList<LibraryEntry> SavedEntries =>
        [.. FilterLibraryState.Value.Entries
            .Where(e => e.Origin == LibraryEntryOrigin.UserSaved)
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)];

    internal static (LibraryEntryId? TargetId, bool FallbackToActiveTab) DecidePendingFocusAfterRemoval(
        IReadOnlyList<LibraryEntry> snapshot,
        LibraryEntryId removedEntryId)
    {
        var idx = -1;

        for (int i = 0; i < snapshot.Count; i++)
        {
            if (snapshot[i].Id.Equals(removedEntryId)) { idx = i; break; }
        }

        if (idx < 0) { return (null, true); }

        if (idx + 1 < snapshot.Count) { return (snapshot[idx + 1].Id, false); }

        if (idx > 0) { return (snapshot[idx - 1].Id, false); }

        return (null, true);
    }

    internal void RecordPendingFocusAfterRemoval(LibraryEntryId removedEntryId)
    {
        var (targetId, fallback) = DecidePendingFocusAfterRemoval(CurrentTabEntries, removedEntryId);

        _pendingFocusTargetEntryId = targetId;
        _pendingFocusToActiveTab = fallback;
    }

    protected override async ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing && _tabKeyModule is not null)
        {
            try
            {
                await _tabKeyModule.InvokeVoidAsync("detach", _tablistRef);
                await _tabKeyModule.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
            catch (JSException) { }
            catch (ObjectDisposedException) { }
        }

        await base.DisposeAsyncCore(disposing);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                _tabKeyModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
                    "import",
                    "./_content/EventLogExpert.UI/FilterLibrary/FilterLibraryModal.js");
            }
            catch (JSDisconnectedException) { }
            catch (JSException) { }
        }

        bool tablistRendered = FilterLibraryState.Value.IsLoaded && !FilterLibraryState.Value.LoadError;

        if (!tablistRendered)
        {
            if (_tabKeyShimAttached && _tabKeyModule is not null)
            {
                try
                {
                    await _tabKeyModule.InvokeVoidAsync("detach", _tablistRef);
                }
                catch (JSDisconnectedException) { }
                catch (JSException) { }
            }

            _tabKeyShimAttached = false;
        }
        else if (_tabKeyModule is not null && !_tabKeyShimAttached)
        {
            try
            {
                await _tabKeyModule.InvokeVoidAsync("attach", _tablistRef);
                _tabKeyShimAttached = true;
            }
            catch (JSDisconnectedException) { }
            catch (JSException) { }
        }

        PruneStaleRowRefs();

        bool focused = false;

        if (_pendingFocusTargetEntryId is { } entryId)
        {
            _pendingFocusTargetEntryId = null;

            if (_rowRefs.TryGetValue(entryId, out var rowRef) && rowRef is not null)
            {
                focused = await rowRef.FocusMoreActionsButtonAsync();

                if (focused)
                {
                    _pendingFocusTab = null;
                    _pendingFocusToActiveTab = false;
                }
                else
                {
                    _pendingFocusToActiveTab = true;
                    _pendingFocusTab = null;
                }
            }
            else
            {
                _pendingFocusToActiveTab = true;
                _pendingFocusTab = null;
            }
        }

        if (!focused)
        {
            if (_pendingFocusTab is { } tab)
            {
                _pendingFocusTab = null;

                if (_tabButtonRefs.TryGetValue(tab, out var tabRef))
                {
                    focused = await ElementFocus.TrySafelyAsync(tabRef);
                }

                _pendingFocusToActiveTab = !focused;
            }

            if (!focused && _pendingFocusToActiveTab)
            {
                _pendingFocusToActiveTab = false;

                if (_tabButtonRefs.TryGetValue(_activeTab, out var activeTabRef))
                {
                    await ElementFocus.SafelyAsync(activeTabRef);
                }
            }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        if (InitialTab is { } tab)
        {
            _activeTab = tab;
        }

        if (!FilterLibraryState.Value.IsLoaded || FilterLibraryState.Value.LoadError)
        {
            FilterLibraryCommands.LoadLibrary();
        }
    }

    private static LibraryTab NextTab(LibraryTab current)
    {
        var index = Array.FindIndex(s_tabs, t => t.Tab == current);

        return index < s_tabs.Length - 1 ? s_tabs[index + 1].Tab : s_tabs[0].Tab;
    }

    private static LibraryTab PrevTab(LibraryTab current)
    {
        var index = Array.FindIndex(s_tabs, t => t.Tab == current);

        return index > 0 ? s_tabs[index - 1].Tab : s_tabs[^1].Tab;
    }

    private int GetCount(LibraryTab tab) => tab switch
    {
        LibraryTab.Favorites => FavoriteEntries.Count,
        LibraryTab.PreviouslyUsed => PreviouslyUsedEntries.Count,
        _ => SavedEntries.Count,
    };

    private Task HandleAddToFilterSetAsync(AddToFilterSetIntent intent)
    {
        if (intent.FilterSetId is { } filterSetId)
        {
            FilterLibraryCommands.AddFilterToExistingFilterSet(filterSetId, intent.Filter, intent.SourceEntryId);
        }
        else if (!string.IsNullOrWhiteSpace(intent.NewFilterSetName))
        {
            FilterLibraryCommands.AddFilterToNewFilterSet(intent.NewFilterSetName!, intent.Filter, intent.SourceEntryId);
        }

        return Task.CompletedTask;
    }

    private async Task HandleApplyAsync(LibraryEntryId id)
    {
        FilterLibraryCommands.ApplyEntry(id);
        var entry = FilterLibraryState.Value.Entries.FirstOrDefault(e => e.Id.Equals(id));
        if (entry is not null) { AnnouncementService.Announce($"Applied {entry.Name}"); }
        await CompleteAsync(true);
    }

    private void HandleDelete(LibraryEntryId id) => FilterLibraryCommands.DeleteEntry(id);

    private async Task HandleReplaceAsync(LibraryEntryId id)
    {
        FilterLibraryCommands.ReplaceWithEntry(id);
        var entry = FilterLibraryState.Value.Entries.FirstOrDefault(e => e.Id.Equals(id));
        if (entry is not null) { AnnouncementService.Announce($"Replaced filters with {entry.Name}"); }
        await CompleteAsync(true);
    }

    private Task HandleRequestPendingFocusAsync(LibraryEntryId entryId)
    {
        RecordPendingFocusAfterRemoval(entryId);

        return Task.CompletedTask;
    }

    private void HandleSaveToLibrary(LibraryEntryId id) => FilterLibraryCommands.SaveEntry(id);

    private void HandleToggleFavorite(FavoriteToggleIntent intent) =>
        FilterLibraryCommands.SetIsFavorite(intent.EntryId, intent.NewIsFavorite);

    private async Task OnTabKeyDown(KeyboardEventArgs e, LibraryTab tab)
    {
        LibraryTab target = tab;
        bool handled = true;

        switch (e.Key)
        {
            case "ArrowRight":
                target = NextTab(tab);
                break;
            case "ArrowLeft":
                target = PrevTab(tab);
                break;
            case "Home":
                target = s_tabs[0].Tab;
                break;
            case "End":
                target = s_tabs[^1].Tab;
                break;
            default:
                handled = false;
                break;
        }

        if (!handled || target == tab) { return; }

        _activeTab = target;
        _pendingFocusTab = target;

        await Task.CompletedTask;
    }

    private void PruneStaleRowRefs()
    {
        if (_rowRefs.Count == 0) { return; }

        var currentIds = new HashSet<LibraryEntryId>(CurrentTabEntries.Select(e => e.Id));
        var stale = _rowRefs.Keys.Where(id => !currentIds.Contains(id)).ToList();

        foreach (var id in stale) { _rowRefs.Remove(id); }
    }

    private void RetryLoad() => FilterLibraryCommands.LoadLibrary();

    private void SetActiveTab(LibraryTab tab)
    {
        if (_activeTab == tab) { return; }

        _activeTab = tab;
    }
}
