// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.UI.FilterLibrary;
using Fluxor;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.FilterLibrary;

public sealed class LibraryEntryRowTests : BunitContext
{
    private readonly IAlertDialogService _alerts = Substitute.For<IAlertDialogService>();
    private readonly IAnnouncementService _announcements = Substitute.For<IAnnouncementService>();
    private readonly IFilterLibraryCommands _commands = Substitute.For<IFilterLibraryCommands>();
    private readonly FilterLibraryState _libraryState = new();
    private readonly IMenuService _menuService = Substitute.For<IMenuService>();

    private FilterPaneState _paneState = new();

    public LibraryEntryRowTests()
    {
        Services.AddSingleton(_alerts);
        Services.AddSingleton(_announcements);
        Services.AddSingleton(_commands);
        Services.AddSingleton(_menuService);

        var paneStateMock = Substitute.For<IState<FilterPaneState>>();
        paneStateMock.Value.Returns(_ => _paneState);
        Services.AddSingleton(paneStateMock);

        var libraryStateMock = Substitute.For<IState<FilterLibraryState>>();
        libraryStateMock.Value.Returns(_ => _libraryState);
        Services.AddSingleton(libraryStateMock);

        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<MenuAnchorRect>("getMenuElementRect", _ => true)
            .SetResult(new MenuAnchorRect(0, 0, 0, 0, 0, 0));
    }

    [Fact]
    public async Task AddToFilterSetSubmenu_WithFilterSets_HasNewSeparatorAndFilterSets()
    {
        var entry = BuildSavedFilter("X");
        var filterSet = BuildFilterSet("P1");
        var component = RenderRow(entry, AllFilterSets: [filterSet]);

        var items = await CapturedMoreMenuItemsAsync(component);
        var sub = items.First(i => i.Label == "Add to filter set...").Children!;

        Assert.Equal(3, sub.Count); // "+ New filter set...", separator, "P1"
        Assert.Equal("+ New filter set...", sub[0].Label);
        Assert.True(sub[1].IsSeparator);
        Assert.Equal("P1", sub[2].Label);
    }

    [Fact]
    public async Task AddToFilterSetSubmenu_WithNoFilterSets_OnlyHasNewFilterSetItem()
    {
        var entry = BuildSavedFilter("X");
        var component = RenderRow(entry, AllFilterSets: []);

        var items = await CapturedMoreMenuItemsAsync(component);
        var sub = items.First(i => i.Label == "Add to filter set...").Children;

        Assert.NotNull(sub);
        Assert.Single(sub);
        Assert.Equal("+ New filter set...", sub[0].Label);
    }

    [Fact]
    public async Task ApplyClick_InvokesOnApplyWithEntryId()
    {
        var entry = BuildSavedFilter("X");
        LibraryEntryId? captured = null;
        var component = RenderRow(entry, onApply: id => { captured = id; return Task.CompletedTask; });

        await component.Find("button.button-green").ClickAsync(new MouseEventArgs());

        Assert.Equal(entry.Id, captured);
    }

    [Fact]
    public async Task DeleteOnAutoTrackedFilter_NoConfirm_InvokesPendingFocusThenDelete()
    {
        var entry = BuildAutoTrackedFilterEntry("X");
        var calls = new List<string>();
        var component = RenderRow(
            entry,
            onDelete: id => { calls.Add("delete"); return Task.CompletedTask; },
            onRequestPendingFocus: id => { calls.Add("focus"); return Task.CompletedTask; });

        var items = await CapturedMoreMenuItemsAsync(component);
        await items.First(i => i.Label == "Delete").OnClickAsync!.Invoke();

        Assert.Equal(["focus", "delete"], calls);
    }

    [Fact]
    public async Task DeleteOnFilterSet_ShowsConfirm()
    {
        var filterSet = BuildFilterSet("P", filterCount: 2);
        var component = RenderRow(filterSet);
        _alerts.ShowAlert(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        var items = await CapturedMoreMenuItemsAsync(component);
        await items.First(i => i.Label == "Delete").OnClickAsync!.Invoke();

        await _alerts.Received(1).ShowAlert("Delete from library?", Arg.Is<string>(m => m.Contains("filter set 'P' with 2 filters")), "Delete", "Cancel");
    }

    [Fact]
    public async Task DeleteOnUserSavedFilter_ShowsConfirm_InvokesOnlyOnAccept()
    {
        var entry = BuildSavedFilter("X");
        bool deleted = false;
        var component = RenderRow(entry, onDelete: id => { deleted = true; return Task.CompletedTask; });
        _alerts.ShowAlert("Delete from library?", Arg.Any<string>(), "Delete", "Cancel").Returns(false);

        var items = await CapturedMoreMenuItemsAsync(component);
        await items.First(i => i.Label == "Delete").OnClickAsync!.Invoke();

        Assert.False(deleted);
    }

    [Fact]
    public void DisposeAsync_UnsubscribesFromMenuServiceStateChanged()
    {
        var entry = BuildSavedFilter("X");
        var component = RenderRow(entry);
        component.Dispose();
        _menuService.StateChanged += Raise.Event<Action>();
    }

    [Fact]
    public async Task ExistingFilterSetSelected_InvokesAddToFilterSetWithFilterSetId()
    {
        var entry = BuildSavedFilter("X");
        var filterSet = BuildFilterSet("P1");
        AddToFilterSetIntent? captured = null;
        var component = RenderRow(
            entry,
            AllFilterSets: [filterSet],
            OnAddToFilterSet: i => { captured = i; return Task.CompletedTask; });

        var items = await CapturedMoreMenuItemsAsync(component);
        var p1Item = items.First(i => i.Label == "Add to filter set...").Children!.First(c => c.Label == "P1");
        await p1Item.OnClickAsync!.Invoke();

        Assert.NotNull(captured);
        Assert.Equal(filterSet.Id, captured.FilterSetId);
        Assert.Null(captured.NewFilterSetName);
    }

    [Fact]
    public void FavoriteButton_AriaPressedReflectsState()
    {
        var entry = BuildSavedFilter("X") with { IsFavorite = true };
        var component = RenderRow(entry);

        Assert.Equal("true", component.Find("button.button-yellow").GetAttribute("aria-pressed"));
    }

    [Fact]
    public async Task FavoriteClick_InvokesOnToggleFavoriteWithNewState()
    {
        var entry = BuildSavedFilter("X");
        FavoriteToggleIntent? captured = null;
        var component = RenderRow(entry, onToggleFavorite: i => { captured = i; return Task.CompletedTask; });

        await component.Find("button.button-yellow").ClickAsync(new MouseEventArgs());

        Assert.NotNull(captured);
        Assert.Equal(entry.Id, captured.EntryId);
        Assert.True(captured.NewIsFavorite);
        _announcements.Received(1).Announce(Arg.Is<string>(s => s.Contains("Marked X as favorite")));
    }

    [Fact]
    public async Task FavoriteOnPreviouslyUsedTab_InvokesPendingFocusBeforeToggle()
    {
        var entry = BuildAutoTrackedFilterEntry("X");
        var calls = new List<string>();
        var component = RenderRow(
            entry,
            activeTab: LibraryTab.PreviouslyUsed,
            onRequestPendingFocus: id => { calls.Add("focus"); return Task.CompletedTask; },
            onToggleFavorite: i => { calls.Add("toggle"); return Task.CompletedTask; });

        await component.Find("button.button-yellow").ClickAsync(new MouseEventArgs());

        Assert.Equal(["focus", "toggle"], calls);
    }

    [Fact]
    public async Task FavoriteOnSavedTab_UserSavedFilter_InvokesPendingFocusBeforeToggle()
    {
        var entry = BuildSavedFilter("X");
        var calls = new List<string>();
        var component = RenderRow(
            entry,
            activeTab: LibraryTab.Saved,
            onRequestPendingFocus: id => { calls.Add("focus"); return Task.CompletedTask; },
            onToggleFavorite: i => { calls.Add("toggle"); return Task.CompletedTask; });

        await component.Find("button.button-yellow").ClickAsync(new MouseEventArgs());

        Assert.Equal(["focus", "toggle"], calls);
    }

    [Fact]
    public void FilterSetEntry_DoesNotRenderFavoriteButton()
    {
        var filterSet = BuildFilterSet("P");
        var component = RenderRow(filterSet);

        Assert.Empty(component.FindAll("button.button-yellow"));
    }

    [Fact]
    public void MoreButton_HasAriaLabel_AriaHaspopup_AriaExpandedFalseInitial()
    {
        var entry = BuildSavedFilter("X");
        var component = RenderRow(entry);

        var more = component.Find("button[aria-label^='More actions for']");
        Assert.Contains("More actions for X", more.GetAttribute("aria-label"));
        Assert.Equal("menu", more.GetAttribute("aria-haspopup"));
        Assert.Equal("false", more.GetAttribute("aria-expanded"));
    }

    [Fact]
    public async Task MoreButtonClick_OpensMenuViaIMenuServiceWithAnchorCoords()
    {
        var entry = BuildSavedFilter("X");
        var component = RenderRow(entry);

        await component.Find("button[aria-label^='More actions for']").ClickAsync(new MouseEventArgs());

        _menuService.Received(1).OpenAt(
            Arg.Any<double>(),
            Arg.Any<double>(),
            Arg.Any<IReadOnlyList<MenuItem>>());
    }

    [Fact]
    public async Task MoreMenu_OnAutoTrackedFilter_IncludesSaveToLibraryItem()
    {
        var entry = BuildAutoTrackedFilterEntry("X");
        var component = RenderRow(entry);

        var items = await CapturedMoreMenuItemsAsync(component);

        Assert.Contains(items, i => i.Label == "Save to Library");
    }

    [Fact]
    public async Task MoreMenu_OnFavoritedAutoTracked_OmitsSaveToLibraryItem()
    {
        var entry = BuildAutoTrackedFilterEntry("X") with { IsFavorite = true };
        var component = RenderRow(entry);

        var items = await CapturedMoreMenuItemsAsync(component);

        Assert.DoesNotContain(items, i => i.Label == "Save to Library");
    }

    [Fact]
    public async Task MoreMenu_OnFilterEntry_IncludesAddToFilterSetSubmenu()
    {
        var entry = BuildSavedFilter("X");
        var component = RenderRow(entry);

        var items = await CapturedMoreMenuItemsAsync(component);
        var addToFilterSet = items.FirstOrDefault(i => i.Label == "Add to filter set...");

        Assert.NotNull(addToFilterSet);
        Assert.NotNull(addToFilterSet.Children);
    }

    [Fact]
    public async Task MoreMenu_OnFilterSetEntry_OmitsAddToFilterSetItem()
    {
        var filterSet = BuildFilterSet("P");
        var component = RenderRow(filterSet);

        var items = await CapturedMoreMenuItemsAsync(component);

        Assert.DoesNotContain(items, i => i.Label == "Add to filter set...");
    }

    [Fact]
    public async Task MoreMenu_OnUserSavedFilter_OmitsSaveToLibraryItem()
    {
        var entry = BuildSavedFilter("X");
        var component = RenderRow(entry);

        var items = await CapturedMoreMenuItemsAsync(component);

        Assert.DoesNotContain(items, i => i.Label == "Save to Library");
    }

    [Fact]
    public async Task NewFilterSetSelected_PromptCancelled_DoesNotInvokeCallback()
    {
        var entry = BuildSavedFilter("X");
        bool invoked = false;
        var component = RenderRow(entry, OnAddToFilterSet: _ => { invoked = true; return Task.CompletedTask; });
        _alerts.DisplayPrompt(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns("");

        var items = await CapturedMoreMenuItemsAsync(component);
        await items.First(i => i.Label == "Add to filter set...").Children!.First().OnClickAsync!.Invoke();

        Assert.False(invoked);
    }

    [Fact]
    public async Task NewFilterSetSelected_PromptReturnsName_InvokesAddToFilterSetWithNewName()
    {
        var entry = BuildSavedFilter("X");
        AddToFilterSetIntent? captured = null;
        var component = RenderRow(entry, OnAddToFilterSet: i => { captured = i; return Task.CompletedTask; });
        _alerts.DisplayPrompt(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns("My Preset");

        var items = await CapturedMoreMenuItemsAsync(component);
        await items.First(i => i.Label == "Add to filter set...").Children!.First().OnClickAsync!.Invoke();

        Assert.NotNull(captured);
        Assert.Null(captured.FilterSetId);
        Assert.Equal("My Preset", captured.NewFilterSetName);
    }

    [Fact]
    public void Render_FilterEntry_ShowsKindIcon()
    {
        var entry = BuildSavedFilter("F");
        var component = RenderRow(entry);

        Assert.Contains("bi-funnel", component.Find("i.library-entry-kind-icon").GetAttribute("class"));
    }

    [Fact]
    public void Render_FilterSetEntry_ShowsFilterSetIconAndFiltersCount()
    {
        var filterSet = BuildFilterSet("P", filterCount: 3);
        var component = RenderRow(filterSet);

        Assert.Contains("bi-collection", component.Find("i.library-entry-kind-icon").GetAttribute("class"));
        Assert.Contains("(3 filters)", component.Find(".library-entry-name").TextContent);
    }

    [Fact]
    public async Task ReplaceOnEmptyPane_NoConfirm_InvokesOnReplace()
    {
        var entry = BuildSavedFilter("X");
        LibraryEntryId? captured = null;
        var component = RenderRow(entry, onReplace: id => { captured = id; return Task.CompletedTask; });

        var items = await CapturedMoreMenuItemsAsync(component);
        var replace = items.First(i => i.Label == "Replace current filters");
        await replace.OnClickAsync!.Invoke();

        Assert.Equal(entry.Id, captured);
        await _alerts.DidNotReceiveWithAnyArgs().ShowAlert(default!, default!, default!, default(string)!);
    }

    [Fact]
    public async Task ReplaceOnNonEmptyPane_ShowsConfirm_InvokesOnlyOnAccept()
    {
        var filter = SavedFilter.TryCreate("Level == 9")!;
        SetPaneFilters([filter]);

        var entry = BuildSavedFilter("X");
        bool replaced = false;
        var component = RenderRow(entry, onReplace: id => { replaced = true; return Task.CompletedTask; });

        _alerts.ShowAlert("Replace current filters?", Arg.Any<string>(), "Replace", "Cancel").Returns(false);

        var items = await CapturedMoreMenuItemsAsync(component);
        await items.First(i => i.Label == "Replace current filters").OnClickAsync!.Invoke();

        Assert.False(replaced);
    }

    [Fact]
    public async Task SaveToLibrary_InvokesCallbackAndAnnounces()
    {
        var entry = BuildAutoTrackedFilterEntry("X");
        bool invoked = false;
        var component = RenderRow(entry, onSaveToLibrary: id => { invoked = true; return Task.CompletedTask; });

        var items = await CapturedMoreMenuItemsAsync(component);
        await items.First(i => i.Label == "Save to Library").OnClickAsync!.Invoke();

        Assert.True(invoked);
        _announcements.Received(1).Announce(Arg.Is<string>(s => s.Contains("Saved X to library")));
    }

    [Fact]
    public async Task UnfavoriteOnFavoritesTab_InvokesPendingFocusBeforeToggle()
    {
        var entry = BuildSavedFilter("X") with { IsFavorite = true };
        var calls = new List<string>();
        var component = RenderRow(
            entry,
            activeTab: LibraryTab.Favorites,
            onRequestPendingFocus: id => { calls.Add("focus"); return Task.CompletedTask; },
            onToggleFavorite: i => { calls.Add("toggle"); return Task.CompletedTask; });

        await component.Find("button.button-yellow").ClickAsync(new MouseEventArgs());

        Assert.Equal(["focus", "toggle"], calls);
    }

    private static LibraryEntrySavedFilter BuildAutoTrackedFilterEntry(string name)
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);

        return new LibraryEntrySavedFilter
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filter = filter,
            Origin = LibraryEntryOrigin.AutoTracked,
            LastUsedUtc = DateTimeOffset.UtcNow,
        };
    }

    private static LibraryEntrySavedFilter BuildFilterEntry(string name)
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);

        return new LibraryEntrySavedFilter
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filter = filter,
        };
    }

    private static LibraryEntryFilterSet BuildFilterSet(string name, int filterCount = 1)
    {
        var filters = new List<SavedFilter>();
        for (var i = 0; i < filterCount; i++) { filters.Add(SavedFilter.TryCreate($"Level == {i}")!); }

        return new LibraryEntryFilterSet
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [.. filters],
        };
    }

    private static LibraryEntrySavedFilter BuildSavedFilter(string name) =>
        BuildFilterEntry(name) with { Origin = LibraryEntryOrigin.UserSaved };

    private async Task<IReadOnlyList<MenuItem>> CapturedMoreMenuItemsAsync(IRenderedComponent<LibraryEntryRow> component)
    {
        IReadOnlyList<MenuItem>? captured = null;
        _menuService.WhenForAnyArgs(s => s.OpenAt(0, 0, null!, false, false))
            .Do(call => captured = (IReadOnlyList<MenuItem>)call[2]!);
        await component.Find("button[aria-label^='More actions for']").ClickAsync(new MouseEventArgs());
        Assert.NotNull(captured);
        return captured;
    }

    private IRenderedComponent<LibraryEntryRow> RenderRow(
        LibraryEntry entry,
        LibraryTab activeTab = LibraryTab.Saved,
        IReadOnlyList<LibraryEntryFilterSet>? AllFilterSets = null,
        Func<LibraryEntryId, Task>? onApply = null,
        Func<LibraryEntryId, Task>? onReplace = null,
        Func<LibraryEntryId, Task>? onDelete = null,
        Func<FavoriteToggleIntent, Task>? onToggleFavorite = null,
        Func<LibraryEntryId, Task>? onSaveToLibrary = null,
        Func<AddToFilterSetIntent, Task>? OnAddToFilterSet = null,
        Func<LibraryEntryId, Task>? onRequestPendingFocus = null) =>
        Render<LibraryEntryRow>(parameters => parameters
            .Add(p => p.Entry, entry)
            .Add(p => p.ActiveTab, activeTab)
            .Add(p => p.AllFilterSets, AllFilterSets ?? [])
            .Add(p => p.OnApply, onApply ?? (_ => Task.CompletedTask))
            .Add(p => p.OnReplace, onReplace ?? (_ => Task.CompletedTask))
            .Add(p => p.OnDelete, onDelete ?? (_ => Task.CompletedTask))
            .Add(p => p.OnToggleFavorite, onToggleFavorite ?? (_ => Task.CompletedTask))
            .Add(p => p.OnSaveToLibrary, onSaveToLibrary ?? (_ => Task.CompletedTask))
            .Add(p => p.OnAddToFilterSet, OnAddToFilterSet ?? (_ => Task.CompletedTask))
            .Add(p => p.OnRequestPendingFocus, onRequestPendingFocus ?? (_ => Task.CompletedTask)));

    private void SetPaneFilters(IEnumerable<SavedFilter> filters)
    {
        _paneState = new FilterPaneState { Filters = [.. filters] };
    }
}
