// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Localization;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.UI.FilterLibrary;
using EventLogExpert.UI.Menu;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using NSubstitute;
using System.Collections.Immutable;

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
        Services.AddSingleton<IStringLocalizer<SharedResource>>(new MarkerLocalizer());

        var activeFilters = Substitute.For<IActiveFiltersSource>();
        activeFilters.Current.Returns(_ => _paneState.Filters);
        Services.AddSingleton(activeFilters);

        var libraryEntries = Substitute.For<ILibraryEntriesSource>();
        libraryEntries.Current.Returns(_ => _libraryState.Entries);
        Services.AddSingleton(libraryEntries);

        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/EventLogExpert.UI/Menu/MenuAnchor.js")
            .Setup<MenuAnchorRect>("getMenuElementRect", _ => true)
            .SetResult(new MenuAnchorRect(0, 0, 0, 0, 0, 0));
    }

    [Fact]
    public async Task AddToFilterSetSubmenu_WithFilterSets_HasNewSeparatorAndFilterSets()
    {
        var entry = BuildSavedFilter("X");
        var filterSet = BuildFilterSet("P1");
        var component = RenderRow(entry, AllFilterSets: [filterSet]);

        var items = await CapturedMoreMenuItemsAsync(component);
        var sub = items.First(i => i.Label == "[[FilterLibrary_Entry_AddToFilterSetMenu]]").Children!;

        Assert.Equal(3, sub.Count);
        Assert.Equal("[[FilterLibrary_Entry_NewFilterSetMenu]]", sub[0].Label);
        Assert.True(sub[1].IsSeparator);
        Assert.Equal("P1", sub[2].Label);
    }

    [Fact]
    public async Task AddToFilterSetSubmenu_WithNoFilterSets_OnlyHasNewFilterSetItem()
    {
        var entry = BuildSavedFilter("X");
        var component = RenderRow(entry, AllFilterSets: []);

        var items = await CapturedMoreMenuItemsAsync(component);
        var sub = items.First(i => i.Label == "[[FilterLibrary_Entry_AddToFilterSetMenu]]").Children;

        Assert.NotNull(sub);
        Assert.Single(sub);
        Assert.Equal("[[FilterLibrary_Entry_NewFilterSetMenu]]", sub[0].Label);
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
        await items.First(i => i.Label == "[[FilterLibrary_Entry_DeleteMenu]]").OnClickAsync!.Invoke();

        Assert.Equal(["focus", "delete"], calls);
    }

    [Fact]
    public async Task DeleteOnFilterSet_ShowsConfirm()
    {
        var filterSet = BuildFilterSet("P", filterCount: 2);
        var component = RenderRow(filterSet);
        _alerts.ShowAlert(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        var items = await CapturedMoreMenuItemsAsync(component);
        await items.First(i => i.Label == "[[FilterLibrary_Entry_DeleteMenu]]").OnClickAsync!.Invoke();

        await _alerts.Received(1).ShowAlert(
            "[[FilterLibrary_Entry_DeleteTitle]]",
            "[[FilterLibrary_Entry_DeleteFilterSetMessage_Many(P|2)]]",
            "[[FilterLibrary_Entry_DeleteMenu]]",
            "[[Modal_Cancel]]");
    }

    [Fact]
    public async Task DeleteOnFilterSet_WithOneFilter_RoutesSingularFilterSetConfirmMessage()
    {
        var filterSet = BuildFilterSet("Solo", filterCount: 1);
        var component = RenderRow(filterSet);
        _alerts.ShowAlert(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        var items = await CapturedMoreMenuItemsAsync(component);
        await items.First(i => i.Label == "[[FilterLibrary_Entry_DeleteMenu]]").OnClickAsync!.Invoke();

        await _alerts.Received(1).ShowAlert(
            "[[FilterLibrary_Entry_DeleteTitle]]",
            "[[FilterLibrary_Entry_DeleteFilterSetMessage_One(Solo|1)]]",
            "[[FilterLibrary_Entry_DeleteMenu]]",
            "[[Modal_Cancel]]");
    }

    [Fact]
    public async Task DeleteOnUserSavedFilter_ShowsConfirm_InvokesOnlyOnAccept()
    {
        var entry = BuildSavedFilter("O'Brien");
        bool deleted = false;
        var component = RenderRow(entry, onDelete: id => { deleted = true; return Task.CompletedTask; });
        _alerts.ShowAlert(
            "[[FilterLibrary_Entry_DeleteTitle]]",
            "[[FilterLibrary_Entry_DeleteFilterMessage(O'Brien)]]",
            "[[FilterLibrary_Entry_DeleteMenu]]",
            "[[Modal_Cancel]]").Returns(false);

        var items = await CapturedMoreMenuItemsAsync(component);
        await items.First(i => i.Label == "[[FilterLibrary_Entry_DeleteMenu]]").OnClickAsync!.Invoke();

        Assert.False(deleted);
    }

    [Fact]
    public async Task DisposeAsync_InvokesOnDisposedWithTabAndEntryId()
    {
        (LibraryTab Tab, LibraryEntryId Id)? disposed = null;
        var entry = BuildSavedFilter("X");
        var component = Render<LibraryEntryRow>(parameters => parameters
            .Add(p => p.Entry, entry)
            .Add(p => p.ActiveTab, LibraryTab.Saved)
            .Add(p => p.AllFilterSets, Array.Empty<LibraryEntryFilterSet>())
            .Add(p => p.OnApply, _ => Task.CompletedTask)
            .Add(p => p.OnReplace, _ => Task.CompletedTask)
            .Add(p => p.OnDelete, _ => Task.CompletedTask)
            .Add(p => p.OnToggleFavorite, _ => Task.CompletedTask)
            .Add(p => p.OnSaveToLibrary, _ => Task.CompletedTask)
            .Add(p => p.OnAddToFilterSet, _ => Task.CompletedTask)
            .Add(p => p.OnRequestPendingFocus, _ => Task.CompletedTask)
            .Add(p => p.OnDisposed, key => disposed = key));

        await component.Instance.DisposeAsync();

        Assert.True(disposed.HasValue);
        Assert.Equal(LibraryTab.Saved, disposed.Value.Tab);
        Assert.Equal(entry.Id, disposed.Value.Id);
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
    public async Task EnterTagEditMode_ScrollInteropDisconnected_IsSwallowed()
    {
        var rowModule = JSInterop.SetupModule("./_content/EventLogExpert.UI/FilterLibrary/LibraryEntryRow.razor.js");
        rowModule.SetupVoid("scrollElementIntoView", _ => true)
            .SetException(new JSDisconnectedException("Circuit disconnected."));
        var entry = BuildSavedFilter("X") with { Tags = ["bug"] };
        var component = RenderRow(entry);

        var exception = await Record.ExceptionAsync(() =>
            component.Find(".library-entry-tag-add-inline").ClickAsync(new MouseEventArgs()));

        Assert.Null(exception);
        rowModule.VerifyInvoke("scrollElementIntoView");
        Assert.NotNull(component.Find(".library-entry-tags-done"));
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
        var p1Item = items.First(i => i.Label == "[[FilterLibrary_Entry_AddToFilterSetMenu]]").Children!.First(c => c.Label == "P1");
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
        _announcements.Received(1).Announce(Arg.Is<string>(s => s != null && s.Contains("[[FilterLibrary_Entry_MarkedFavoriteAnnouncement(X)]]")));
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
    public void FavoriteToggle_RoutesAddAndRemoveStatesThroughDistinctAriaAndTooltipKeys()
    {
        var notFavorite = RenderRow(BuildSavedFilter("Entry"));
        var notFavoriteButton = notFavorite.Find("button.button-yellow");

        Assert.Equal("[[FilterLibrary_Entry_AddFavoriteAria(Entry)]]", notFavoriteButton.GetAttribute("aria-label"));
        Assert.Equal("[[FilterLibrary_Entry_AddFavoriteTitle]]", notFavoriteButton.GetAttribute("data-tooltip"));

        var favorite = RenderRow(BuildSavedFilter("Entry") with { IsFavorite = true });
        var favoriteButton = favorite.Find("button.button-yellow");

        Assert.Equal("[[FilterLibrary_Entry_RemoveFavoriteAria(Entry)]]", favoriteButton.GetAttribute("aria-label"));
        Assert.Equal("[[FilterLibrary_Entry_RemoveFavoriteTitle]]", favoriteButton.GetAttribute("data-tooltip"));
    }

    [Fact]
    public void FilterSetEntry_DoesNotRenderFavoriteButton()
    {
        var filterSet = BuildFilterSet("P");
        var component = RenderRow(filterSet);

        Assert.Empty(component.FindAll("button.button-yellow"));
    }

    [Fact]
    public void KindIcon_RoutesFilterAndFilterSetStatesThroughDistinctAriaKeys()
    {
        var filter = RenderRow(BuildSavedFilter("Filter"));

        Assert.Equal("[[FilterLibrary_Entry_FilterKindAria]]", filter.Find("i.library-entry-kind-icon").GetAttribute("aria-label"));

        var filterSet = RenderRow(BuildFilterSet("Set"));

        Assert.Equal("[[FilterLibrary_Entry_FilterSetKindAria]]", filterSet.Find("i.library-entry-kind-icon").GetAttribute("aria-label"));
    }

    [Fact]
    public void MoreActionsButton_RoutesAriaAndTooltipThroughDistinctKeys()
    {
        var component = RenderRow(BuildSavedFilter("Entry"));
        var button = component.Find("button[aria-haspopup='menu']");

        Assert.Equal("[[FilterLibrary_Entry_MoreActionsAria(Entry)]]", button.GetAttribute("aria-label"));
        Assert.Equal("[[FilterLibrary_Entry_MoreActionsTitle]]", button.GetAttribute("data-tooltip"));
    }

    [Fact]
    public async Task MoreButtonClick_OpensMenuViaIMenuServiceWithAnchorCoords()
    {
        var entry = BuildSavedFilter("X");
        var component = RenderRow(entry);

        await component.Find("button[aria-label^='[[FilterLibrary_Entry_MoreActionsAria']").ClickAsync(new MouseEventArgs { Detail = 1 });

        _menuService.Received(1).OpenAt(
            Arg.Any<double>(),
            Arg.Any<double>(),
            Arg.Any<IReadOnlyList<MenuItem>>());
    }

    [Fact]
    public async Task MoreButtonKeyboardActivation_OpensMenuWithKeyboardFocusFlag()
    {
        var entry = BuildSavedFilter("X");
        var component = RenderRow(entry);

        await component.Find("button[aria-label^='[[FilterLibrary_Entry_MoreActionsAria']").ClickAsync(new MouseEventArgs { Detail = 0 });

        _menuService.Received(1).OpenAt(
            Arg.Any<double>(),
            Arg.Any<double>(),
            Arg.Any<IReadOnlyList<MenuItem>>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            true);
    }

    [Fact]
    public void MoreButton_HasAriaLabel_AriaHaspopup_AriaExpandedFalseInitial()
    {
        var entry = BuildSavedFilter("X");
        var component = RenderRow(entry);

        var more = component.Find("button[aria-label^='[[FilterLibrary_Entry_MoreActionsAria']");
        Assert.Equal("[[FilterLibrary_Entry_MoreActionsAria(X)]]", more.GetAttribute("aria-label"));
        Assert.Equal("menu", more.GetAttribute("aria-haspopup"));
        Assert.Equal("false", more.GetAttribute("aria-expanded"));
    }

    [Fact]
    public async Task MoreMenu_OnAutoTrackedFilter_IncludesSaveToLibraryItem()
    {
        var entry = BuildAutoTrackedFilterEntry("X");
        var component = RenderRow(entry);

        var items = await CapturedMoreMenuItemsAsync(component);

        Assert.Contains(items, i => i.Label == "[[FilterLibrary_Entry_SaveToLibraryMenu]]");
    }

    [Fact]
    public async Task MoreMenu_OnFavoritedAutoTracked_OmitsSaveToLibraryItem()
    {
        var entry = BuildAutoTrackedFilterEntry("X") with { IsFavorite = true };
        var component = RenderRow(entry);

        var items = await CapturedMoreMenuItemsAsync(component);

        Assert.DoesNotContain(items, i => i.Label == "[[FilterLibrary_Entry_SaveToLibraryMenu]]");
    }

    [Fact]
    public async Task MoreMenu_OnFilterEntry_IncludesAddToFilterSetSubmenu()
    {
        var entry = BuildSavedFilter("X");
        var component = RenderRow(entry);

        var items = await CapturedMoreMenuItemsAsync(component);
        var addToFilterSet = items.FirstOrDefault(i => i.Label == "[[FilterLibrary_Entry_AddToFilterSetMenu]]");

        Assert.NotNull(addToFilterSet);
        Assert.NotNull(addToFilterSet.Children);
    }

    [Fact]
    public async Task MoreMenu_OnFilterSetEntry_OmitsAddToFilterSetItem()
    {
        var filterSet = BuildFilterSet("P");
        var component = RenderRow(filterSet);

        var items = await CapturedMoreMenuItemsAsync(component);

        Assert.DoesNotContain(items, i => i.Label == "[[FilterLibrary_Entry_AddToFilterSetMenu]]");
    }

    [Fact]
    public async Task MoreMenu_OnUserSavedFilter_OmitsSaveToLibraryItem()
    {
        var entry = BuildSavedFilter("X");
        var component = RenderRow(entry);

        var items = await CapturedMoreMenuItemsAsync(component);

        Assert.DoesNotContain(items, i => i.Label == "[[FilterLibrary_Entry_SaveToLibraryMenu]]");
    }

    [Fact]
    public void MoreTagsButton_DescribedByIdIsTabScopedUniqueAndFormatSafe()
    {
        // Cap is 2 inline chips, so 4 tags leaves 2 hidden and renders the "+N more" button.
        var entry = BuildSavedFilter("Entry") with { Tags = [.. Enumerable.Range(0, 4).Select(i => $"tag{i}")] };

        var saved = RenderRow(entry, activeTab: LibraryTab.Saved);
        var favorites = RenderRow(entry, activeTab: LibraryTab.Favorites);

        var savedButton = saved.Find(".library-entry-tag-chip-more");
        var favoritesButton = favorites.Find(".library-entry-tag-chip-more");
        var savedId = savedButton.GetAttribute("aria-describedby")!;
        var favoritesId = favoritesButton.GetAttribute("aria-describedby")!;

        // The same entry renders in every library tabpanel at once, so the describedby id MUST include the tab
        // to stay document-unique (D8/§4.C) - otherwise duplicate DOM ids and cross-tab describedby resolution.
        Assert.NotEqual(savedId, favoritesId);

        // The id is built from the Guid "N" format, not the LibraryEntryId record-struct ToString, so it has no
        // whitespace or braces that would break aria-describedby IDREF token resolution.
        Assert.DoesNotContain(savedId, c => char.IsWhiteSpace(c) || c is '{' or '}');

        // describedby resolves to the hidden span, whose text equals the visual data-tooltip (the hidden tags).
        var hint = saved.Find($"#{savedId}");
        Assert.Equal(savedButton.GetAttribute("data-tooltip"), hint.TextContent);
    }

    [Fact]
    public async Task NewFilterSetSelected_PromptCancelled_DoesNotInvokeCallback()
    {
        var entry = BuildSavedFilter("X");
        bool invoked = false;
        var component = RenderRow(entry, OnAddToFilterSet: _ => { invoked = true; return Task.CompletedTask; });
        _alerts.DisplayPrompt(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Func<string, string?>>()).Returns("");

        var items = await CapturedMoreMenuItemsAsync(component);
        await items.First(i => i.Label == "[[FilterLibrary_Entry_AddToFilterSetMenu]]").Children!.First().OnClickAsync!.Invoke();

        Assert.False(invoked);
    }

    [Fact]
    public async Task NewFilterSetSelected_PromptReturnsName_InvokesAddToFilterSetWithNewName()
    {
        var entry = BuildSavedFilter("X");
        AddToFilterSetIntent? captured = null;
        var component = RenderRow(entry, OnAddToFilterSet: i => { captured = i; return Task.CompletedTask; });
        _alerts.DisplayPrompt(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Func<string, string?>>()).Returns("My Preset");

        var items = await CapturedMoreMenuItemsAsync(component);
        await items.First(i => i.Label == "[[FilterLibrary_Entry_AddToFilterSetMenu]]").Children!.First().OnClickAsync!.Invoke();

        Assert.NotNull(captured);
        Assert.Null(captured.FilterSetId);
        Assert.Equal("My Preset", captured.NewFilterSetName);
    }

    [Theory]
    [InlineData(30, "[[FilterLibrary_Entry_RelativeTime_JustNow]]")]
    [InlineData(90, "[[FilterLibrary_Entry_RelativeTime_Minutes(1)]]")]
    [InlineData(7200, "[[FilterLibrary_Entry_RelativeTime_Hours(2)]]")]
    [InlineData(259200, "[[FilterLibrary_Entry_RelativeTime_Days(3)]]")]
    public void PreviouslyUsedRelativeTime_RoutesEachDisplayedRangeThroughExpectedKey(
        int secondsAgo,
        string expectedMarker)
    {
        var entry = BuildAutoTrackedFilterEntry("Recent") with
        {
            LastUsedUtc = DateTimeOffset.UtcNow.AddSeconds(-secondsAgo)
        };
        var component = RenderRow(entry, activeTab: LibraryTab.PreviouslyUsed);

        Assert.Contains(expectedMarker, component.Find(".library-entry-name").TextContent);
    }

    [Fact]
    public void PreviouslyUsedTrackedBadge_RoutesAriaAndTooltipThroughDistinctKeys()
    {
        var entry = BuildAutoTrackedFilterEntry("Tracked");
        var component = RenderRow(entry, activeTab: LibraryTab.PreviouslyUsed);
        var badge = component.Find(".library-entry-tracked-badge");

        Assert.Equal("[[FilterLibrary_Entry_TrackedAria]]", badge.GetAttribute("aria-label"));
        Assert.Equal("[[FilterLibrary_Entry_TrackedTitle]]", badge.GetAttribute("data-tooltip"));
        Assert.Equal("[[FilterLibrary_Entry_TrackedLabel]]", badge.TextContent.Trim());
    }

    [Fact]
    public async Task RemovingInlineTagChip_DispatchesUpdateEntryWithoutThatTag()
    {
        var entry = BuildSavedFilter("X") with { Tags = ["bug", "perf"] };
        var component = RenderRow(entry);

        await component.Find("button[aria-label='[[FilterLibrary_Entry_RemoveTagAria(bug)]]']").ClickAsync(new MouseEventArgs());

        _commands.Received(1).SetEntryTags(entry.Id, Arg.Is<ImmutableList<string>>(
            tags => tags != null && tags.SequenceEqual(new[] { "perf" })));
        _announcements.Received(1).Announce("[[FilterLibrary_Entry_TagRemovedAnnouncement(bug|X)]]");
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
        Assert.Contains("[[FilterLibrary_Entry_FilterCount_Many(3)]]", component.Find(".library-entry-name").TextContent);
    }

    [Fact]
    public void Render_FilterSetEntry_WithOneFilter_RoutesSingularFilterCount()
    {
        var filterSet = BuildFilterSet("P", filterCount: 1);
        var component = RenderRow(filterSet);

        Assert.Contains("[[FilterLibrary_Entry_FilterCount_One(1)]]", component.Find(".library-entry-name").TextContent);
    }

    [Fact]
    public async Task ReplaceOnEmptyPane_NoConfirm_InvokesOnReplace()
    {
        var entry = BuildSavedFilter("X");
        LibraryEntryId? captured = null;
        var component = RenderRow(entry, onReplace: id => { captured = id; return Task.CompletedTask; });

        var items = await CapturedMoreMenuItemsAsync(component);
        var replace = items.First(i => i.Label == "[[FilterLibrary_Entry_ReplaceCurrentMenu]]");
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

        _alerts.ShowAlert("[[FilterLibrary_Entry_ReplacePromptTitle]]", Arg.Any<string>(), "[[FilterPane_Action_Replace]]", "[[Modal_Cancel]]").Returns(false);

        var items = await CapturedMoreMenuItemsAsync(component);
        await items.First(i => i.Label == "[[FilterLibrary_Entry_ReplaceCurrentMenu]]").OnClickAsync!.Invoke();

        Assert.False(replaced);
    }

    [Fact]
    public async Task SaveToLibrary_InvokesCallbackAndAnnounces()
    {
        var entry = BuildAutoTrackedFilterEntry("X");
        bool invoked = false;
        var component = RenderRow(entry, onSaveToLibrary: id => { invoked = true; return Task.CompletedTask; });

        var items = await CapturedMoreMenuItemsAsync(component);
        await items.First(i => i.Label == "[[FilterLibrary_Entry_SaveToLibraryMenu]]").OnClickAsync!.Invoke();

        Assert.True(invoked);
        _announcements.Received(1).Announce(Arg.Is<string>(s => s != null && s.Contains("[[FilterLibrary_Entry_SavedToLibraryAnnouncement(X)]]")));
    }

    [Fact]
    public async Task TagEditDoneButton_RoutesAriaAndTooltipThroughDistinctKeys()
    {
        var entry = BuildSavedFilter("Entry") with { Tags = ["alpha"] };
        var component = RenderRow(entry);

        await component.Find(".library-entry-tag-add-inline").ClickAsync(new MouseEventArgs());

        var doneButton = component.Find(".library-entry-tags-done");
        Assert.Equal("[[FilterLibrary_Entry_DoneEditingTagsAria]]", doneButton.GetAttribute("aria-label"));
        Assert.Equal("[[FilterLibrary_Entry_DoneTitle]]", doneButton.GetAttribute("data-tooltip"));
    }

    [Fact]
    public void TagEditToggle_RoutesAddAndEditStatesThroughDistinctAriaAndTooltipKeys()
    {
        var noTags = RenderRow(BuildSavedFilter("Entry"));
        var addButton = noTags.Find(".library-entry-tag-add-inline");

        Assert.Equal("[[FilterLibrary_Entry_AddTagsAria(Entry)]]", addButton.GetAttribute("aria-label"));
        Assert.Equal("[[FilterLibrary_Entry_AddTagsTitle]]", addButton.GetAttribute("data-tooltip"));

        var withTags = RenderRow(BuildSavedFilter("Entry") with { Tags = ["alpha"] });
        var editButton = withTags.Find(".library-entry-tag-add-inline");

        Assert.Equal("[[FilterLibrary_Entry_EditTagsAria(Entry)]]", editButton.GetAttribute("aria-label"));
        Assert.Equal("[[FilterLibrary_Entry_EditTagsTitle]]", editButton.GetAttribute("data-tooltip"));
    }

    [Fact]
    public async Task TagEditor_RoutesTagPickerPlaceholderAndRemoveChipAriaThroughLocalizer()
    {
        var empty = RenderRow(BuildSavedFilter("Entry"));
        await empty.Find(".library-entry-tag-add-inline").ClickAsync(new MouseEventArgs());

        Assert.Equal(
            "[[FilterLibrary_Entry_TagInputPlaceholder]]",
            empty.Find(".tag-picker-input").GetAttribute("placeholder"));

        var tagged = RenderRow(BuildSavedFilter("Entry") with { Tags = ["alpha"] });
        await tagged.Find(".library-entry-tag-add-inline").ClickAsync(new MouseEventArgs());

        Assert.Equal(
            "[[FilterLibrary_Entry_RemoveTagChipAria]]",
            tagged.Find(".tag-picker-chip-remove").GetAttribute("aria-label"));
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
            .Do(call => captured = call.ArgAt<IReadOnlyList<MenuItem>>(2));
        await component.Find("button[aria-label^='[[FilterLibrary_Entry_MoreActionsAria']").ClickAsync(new MouseEventArgs { Detail = 1 });
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
