// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.UI.FilterLibrary;
using EventLogExpert.UI.Tests.TestUtils;
using Fluxor;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.FilterLibrary;

public sealed class FilterLibraryModalTests : BunitContext
{
    private readonly IAnnouncementService _announcements = Substitute.For<IAnnouncementService>();
    private readonly IFilterLibraryCommands _commands = Substitute.For<IFilterLibraryCommands>();
    private readonly IFilterLibraryExportService _exportService = Substitute.For<IFilterLibraryExportService>();
    private readonly IFilePickerService _filePicker = Substitute.For<IFilePickerService>();
    private readonly IModalCoordinator _modalCoordinator = Substitute.For<IModalCoordinator>();
    private readonly ModalId _modalId = new(1L);
    private readonly IModalService _modalService = Substitute.For<IModalService>();

    public FilterLibraryModalTests()
    {
        Services.AddBannerHostDependencies();
        Services.AddMenuMocks();

        _modalService.ActiveModalId.Returns(_modalId);

        Services.AddSingleton(_announcements);
        Services.AddSingleton(_commands);
        Services.AddSingleton(_modalCoordinator);
        Services.AddSingleton(_modalService);
        Services.AddSingleton(_filePicker);
        Services.AddSingleton(_exportService);

        var paneState = Substitute.For<IState<FilterPaneState>>();
        paneState.Value.Returns(new FilterPaneState());
        Services.AddSingleton(paneState);

        Services.AddSingleton(Substitute.For<IAlertDialogService>());

        Services.AddFluxor(options => options.ScanAssemblies(typeof(FilterLibraryModal).Assembly));

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task ApplyClick_DispatchesApplyAndCompletesModalWithTrue()
    {
        var entry = BuildSavedFilter("S");
        SetState(new FilterLibraryState { Entries = [entry], IsLoaded = true });

        var component = Render<FilterLibraryModal>();
        await component.Find(".library-entry-row button.button-green").ClickAsync(new MouseEventArgs());

        _commands.Received(1).ApplyEntry(entry.Id);
        _modalService.Received(1).Complete(_modalId, Arg.Is<object?>(v => Equals(v, true)));
    }

    [Fact]
    public void BuildPreflightSummary_ImportBlocked_ReturnsGenericMessageWithInvalidNames()
    {
        var preflight = ImportPreflight.Blocked([@"Network\DNS", @"Mail\SMTP"]);

        var summary = FilterLibraryModal.BuildPreflightSummary(preflight);

        Assert.Contains("cannot be imported", summary);
        Assert.Contains(@"Network\DNS", summary);
        Assert.Contains(@"Mail\SMTP", summary);
    }

    [Fact]
    public void DecidePendingFocusAfterRemoval_LastRow_TargetsPrevious()
    {
        var a = BuildSavedFilter("A");
        var b = BuildSavedFilter("B");
        var c = BuildSavedFilter("C");
        var snapshot = new LibraryEntry[] { a, b, c };

        var (targetId, fallback) = FilterLibraryModal.DecidePendingFocusAfterRemoval(snapshot, c.Id);

        Assert.Equal(b.Id, targetId);
        Assert.False(fallback);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void DecidePendingFocusAfterRemoval_MiddleOrFirstRow_TargetsNext(int removedIndex)
    {
        var a = BuildSavedFilter("A");
        var b = BuildSavedFilter("B");
        var c = BuildSavedFilter("C");
        var snapshot = new LibraryEntry[] { a, b, c };
        var removedId = snapshot[removedIndex].Id;
        var expectedNextId = snapshot[removedIndex + 1].Id;

        var (targetId, fallback) = FilterLibraryModal.DecidePendingFocusAfterRemoval(snapshot, removedId);

        Assert.Equal(expectedNextId, targetId);
        Assert.False(fallback);
    }

    [Fact]
    public void DecidePendingFocusAfterRemoval_NotFound_FallsBackToActiveTab()
    {
        var a = BuildSavedFilter("A");
        var b = BuildSavedFilter("B");
        var missing = BuildSavedFilter("Missing");
        var snapshot = new LibraryEntry[] { a, b };

        var (targetId, fallback) = FilterLibraryModal.DecidePendingFocusAfterRemoval(snapshot, missing.Id);

        Assert.Null(targetId);
        Assert.True(fallback);
    }

    [Fact]
    public void DecidePendingFocusAfterRemoval_SoleRow_FallsBackToActiveTab()
    {
        var a = BuildSavedFilter("A");
        var snapshot = new LibraryEntry[] { a };

        var (targetId, fallback) = FilterLibraryModal.DecidePendingFocusAfterRemoval(snapshot, a.Id);

        Assert.Null(targetId);
        Assert.True(fallback);
    }

    [Fact]
    public async Task FavoritesTab_ProjectsOnlyFavorites_SortedByName()
    {
        var s1 = BuildSavedFilter("Saved");
        var f1 = BuildFilterEntry("Beta") with { IsFavorite = true };
        var f2 = BuildFilterEntry("Alpha") with { IsFavorite = true };
        SetState(new FilterLibraryState { Entries = [s1, f1, f2], IsLoaded = true });

        var component = Render<FilterLibraryModal>();
        await component.FindAll("[role='tab']")[1].ClickAsync(new MouseEventArgs());

        var names = component.FindAll(".sidebar-tabs-tabpanel.active .library-entry-name-text").Select(n => n.TextContent.Trim()).ToList();
        Assert.Equal(["Alpha", "Beta"], names);
    }

    [Fact]
    public void OnInitialized_DispatchesLoadLibrary_WhenLoadError()
    {
        SetState(new FilterLibraryState { IsLoaded = true, LoadError = true });

        Render<FilterLibraryModal>();

        _commands.Received().LoadLibrary();
    }

    [Fact]
    public void OnInitialized_DispatchesLoadLibrary_WhenNotLoaded()
    {
        SetState(new FilterLibraryState { IsLoaded = false });

        Render<FilterLibraryModal>();

        _commands.Received().LoadLibrary();
    }

    [Fact]
    public void OnInitialized_DoesNotDispatchLoadLibrary_WhenLoadedAndNoError()
    {
        SetState(new FilterLibraryState { IsLoaded = true, LoadError = false });

        Render<FilterLibraryModal>();

        _commands.DidNotReceive().LoadLibrary();
    }

    [Fact]
    public async Task PreviouslyUsedTab_ProjectsOnlyWithLastUsedUtc_OrderedDescTop50()
    {
        var older = BuildAutoTrackedFilterEntry("Old", DateTimeOffset.UtcNow.AddDays(-2));
        var newer = BuildAutoTrackedFilterEntry("New", DateTimeOffset.UtcNow.AddHours(-1));
        var saved = BuildSavedFilter("Saved"); // No LastUsedUtc → not in Previously Used
        SetState(new FilterLibraryState { Entries = [older, newer, saved], IsLoaded = true });

        var component = Render<FilterLibraryModal>();
        await component.FindAll("[role='tab']")[2].ClickAsync(new MouseEventArgs());

        var names = component.FindAll(".sidebar-tabs-tabpanel.active .library-entry-name-text").Select(n => n.TextContent.Trim()).ToList();
        Assert.Equal(["New", "Old"], names);
    }

    [Fact]
    public void Render_ActiveTab_HasAriaSelectedTrueAndTabindexZero_OthersInert()
    {
        SetState(new FilterLibraryState { IsLoaded = true });

        var component = Render<FilterLibraryModal>();

        var tabs = component.FindAll("[role='tab']");
        Assert.Equal("true", tabs[0].GetAttribute("aria-selected"));
        Assert.Equal("0", tabs[0].GetAttribute("tabindex"));
        Assert.Equal("false", tabs[1].GetAttribute("aria-selected"));
        Assert.Equal("-1", tabs[1].GetAttribute("tabindex"));
    }

    [Theory]
    [InlineData(0, "No saved filters or filter sets")]
    [InlineData(1, "No favorited")]
    [InlineData(2, "No filters have been applied recently")]
    public async Task Render_EmptyTab_ShowsTabSpecificMessage(int tabIndex, string expectedFragment)
    {
        SetState(new FilterLibraryState { IsLoaded = true });

        var component = Render<FilterLibraryModal>();
        if (tabIndex != 0)
        {
            await component.FindAll("[role='tab']")[tabIndex].ClickAsync(new MouseEventArgs());
        }

        var emptyState = component.Find(".sidebar-tabs-tabpanel.active .library-empty-state");
        Assert.Contains(expectedFragment, emptyState.TextContent);
    }

    [Fact]
    public void Render_FavoritesTab_OnlySavedFilterEntries_ExcludesFilterSet()
    {
        var favoritedFilter = BuildSavedFilter("FavFilter") with { IsFavorite = true };
        var favoritedFilterSet = new LibraryEntryFilterSet
        {
            Name = "FavSet",
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [],
            IsFavorite = true,
        };
        SetState(new FilterLibraryState { Entries = [favoritedFilter, favoritedFilterSet], IsLoaded = true });

        var component = Render<FilterLibraryModal>();

        var tabs = component.FindAll("[role='tab']");
        Assert.Contains("Favorites (1)", tabs[1].TextContent);
    }

    [Fact]
    public void Render_HiddenTabpanel_DoesNotRenderRowChildren()
    {
        var saved = BuildSavedFilter("S");
        SetState(new FilterLibraryState { Entries = [saved], IsLoaded = true });

        var component = Render<FilterLibraryModal>();

        // Only the active (Saved) tab should render its row; the other two tabpanels are empty.
        Assert.Single(component.FindAll(".library-entry-row"));
    }

    [Fact]
    public void Render_NoLibraryTags_TagFilterBarNotRendered()
    {
        SetState(new FilterLibraryState { Entries = [BuildSavedFilter("S")], IsLoaded = true });

        var component = Render<FilterLibraryModal>();

        Assert.Empty(component.FindAll(".library-tag-filter-bar"));
    }

    [Fact]
    public void Render_PreviouslyUsedTab_RequiresOriginAutoTracked_ExcludesUserSavedWithLastUsedUtc()
    {
        var autoTracked = BuildAutoTrackedFilterEntry("Recent");
        var userSavedWithLastUsed = BuildSavedFilter("UserWithLastUsed") with { LastUsedUtc = DateTimeOffset.UtcNow };
        SetState(new FilterLibraryState { Entries = [autoTracked, userSavedWithLastUsed], IsLoaded = true });

        var component = Render<FilterLibraryModal>();

        var tabs = component.FindAll("[role='tab']");
        Assert.Contains("Previously Used (1)", tabs[2].TextContent);
    }

    [Fact]
    public void Render_RendersThreeTabs_WithLabelsAndCounts()
    {
        var saved = BuildSavedFilter("Saved");
        var fav = BuildFilterEntry("Favorited") with { IsFavorite = true };
        var recent = BuildAutoTrackedFilterEntry("Recent");
        SetState(new FilterLibraryState { Entries = [saved, fav, recent], IsLoaded = true });

        var component = Render<FilterLibraryModal>();

        var tabs = component.FindAll("[role='tab']");
        Assert.Equal(3, tabs.Count);
        Assert.Contains("Saved (1)", tabs[0].TextContent);
        Assert.Contains("Favorites (1)", tabs[1].TextContent);
        Assert.Contains("Previously Used (1)", tabs[2].TextContent);
    }

    [Fact]
    public void Render_SavedTab_ExcludesFavoritedUserSavedEntries()
    {
        var notFav = BuildSavedFilter("NotFav");
        var fav = BuildSavedFilter("Fav") with { IsFavorite = true };
        SetState(new FilterLibraryState { Entries = [notFav, fav], IsLoaded = true });

        var component = Render<FilterLibraryModal>();

        var names = component.FindAll(".sidebar-tabs-tabpanel.active .library-entry-name-text").Select(n => n.TextContent.Trim()).ToList();
        Assert.Equal(["NotFav"], names);
    }

    [Fact]
    public void Render_SavedTab_ProjectsOnlyUserSavedEntries_SortedByName()
    {
        var s1 = BuildSavedFilter("Beta");
        var s2 = BuildSavedFilter("Alpha");
        var recent = BuildAutoTrackedFilterEntry("Recent");
        SetState(new FilterLibraryState { Entries = [s1, s2, recent], IsLoaded = true });

        var component = Render<FilterLibraryModal>();

        var names = component.FindAll(".sidebar-tabs-tabpanel.active .library-entry-name-text").Select(n => n.TextContent.Trim()).ToList();
        Assert.Equal(["Alpha", "Beta"], names);
    }

    [Fact]
    public void Render_TabpanelTabindex_AbsentWhenRowsPresent()
    {
        SetState(new FilterLibraryState { Entries = [BuildSavedFilter("S")], IsLoaded = true });
        var component = Render<FilterLibraryModal>();
        Assert.Null(component.FindAll("[role='tabpanel']")[0].GetAttribute("tabindex"));
    }

    [Fact]
    public void Render_TabpanelTabindex_ZeroWhenEmpty()
    {
        SetState(new FilterLibraryState { IsLoaded = true }); // empty
        var component = Render<FilterLibraryModal>();
        Assert.Equal("0", component.FindAll("[role='tabpanel']")[0].GetAttribute("tabindex"));
    }

    [Fact]
    public void Render_ThreeTabpanelsInDom_InactiveOnesDisplayNone()
    {
        SetState(new FilterLibraryState { IsLoaded = true });

        var component = Render<FilterLibraryModal>();

        var panels = component.FindAll("[role='tabpanel']");
        Assert.Equal(3, panels.Count);
        // Saved is active by default — its style should not contain display:none
        Assert.DoesNotContain("display: none", panels[0].GetAttribute("style") ?? string.Empty);
        Assert.Contains("display: none", panels[1].GetAttribute("style") ?? string.Empty);
        Assert.Contains("display: none", panels[2].GetAttribute("style") ?? string.Empty);
    }

    [Fact]
    public void Render_WhenLoadError_RendersErrorStateWithRetry()
    {
        SetState(new FilterLibraryState { LoadError = true, IsLoaded = true });

        var component = Render<FilterLibraryModal>();

        Assert.NotNull(component.Find(".filter-library-error"));
        Assert.NotNull(component.Find(".filter-library-error button"));
    }

    [Fact]
    public void Render_WhenNotLoaded_RendersLoadingState()
    {
        SetState(new FilterLibraryState { IsLoaded = false });

        var component = Render<FilterLibraryModal>();

        Assert.NotNull(component.Find(".filter-library-loading"));
        Assert.Equal("true", component.Find(".filter-library-loading").GetAttribute("aria-busy"));
    }

    [Fact]
    public void Render_WithLibraryTags_TagFilterBarRenderedWithChipPerTag()
    {
        var entry = BuildSavedFilter("S") with { Tags = ["alpha", "beta"] };
        SetState(new FilterLibraryState { Entries = [entry], IsLoaded = true });

        var component = Render<FilterLibraryModal>();

        var activePanel = component.Find("[role='tabpanel'].active");
        var chipsInActivePanel = activePanel.QuerySelectorAll(".library-tag-filter-chip");
        Assert.Equal(2, chipsInActivePanel.Length);
        Assert.Equal("alpha", chipsInActivePanel[0].TextContent.Trim());
        Assert.Equal("beta", chipsInActivePanel[1].TextContent.Trim());
    }

    [Fact]
    public async Task RetryButton_DispatchesLoadLibrary()
    {
        SetState(new FilterLibraryState { LoadError = true, IsLoaded = true });

        var component = Render<FilterLibraryModal>();
        await component.Find(".filter-library-error button").ClickAsync(new MouseEventArgs());

        _commands.Received().LoadLibrary();
    }

    [Fact]
    public async Task TabClick_SwitchesActiveTab()
    {
        SetState(new FilterLibraryState { IsLoaded = true });

        var component = Render<FilterLibraryModal>();
        await component.FindAll("[role='tab']")[1].ClickAsync(new MouseEventArgs());

        Assert.Equal("true", component.FindAll("[role='tab']")[1].GetAttribute("aria-selected"));
    }

    [Theory]
    [InlineData("ArrowDown", 1)]
    [InlineData("ArrowUp", 2)]
    [InlineData("Home", 0)]
    [InlineData("End", 2)]
    public async Task TabKeydown_RotatesActiveTab(string key, int expectedTabIndex)
    {
        SetState(new FilterLibraryState { IsLoaded = true });

        var component = Render<FilterLibraryModal>();
        await component.FindAll("[role='tab']")[0].KeyDownAsync(new KeyboardEventArgs { Key = key });

        var tabs = component.FindAll("[role='tab']");
        Assert.Equal("true", tabs[expectedTabIndex].GetAttribute("aria-selected"));
    }

    [Fact]
    public async Task TagFilter_AllTagsSelectedButNoEntryCarriesAll_ShowsFilteredEmptyMessage()
    {
        var onlyAlpha = BuildSavedFilter("onlyAlpha") with { Tags = ["alpha"] };
        var onlyBeta = BuildSavedFilter("onlyBeta") with { Tags = ["beta"] };
        SetState(new FilterLibraryState { Entries = [onlyAlpha, onlyBeta], IsLoaded = true });

        var component = Render<FilterLibraryModal>();

        var chips = component.Find("[role='tabpanel'].active").QuerySelectorAll(".library-tag-filter-chip");
        await chips[0].ClickAsync(new MouseEventArgs());
        await component.Find("[role='tabpanel'].active")
            .QuerySelectorAll(".library-tag-filter-chip")[1]
            .ClickAsync(new MouseEventArgs());

        var empty = component.Find("[role='tabpanel'].active .library-empty-state");
        Assert.Contains("No entries match the selected tags", empty.TextContent);
    }

    [Fact]
    public async Task TagFilter_PreviouslyUsedTab_AppliesBeforeTake50()
    {
        var entries = new List<LibraryEntry>();
        var now = DateTimeOffset.UtcNow;
        for (int i = 0; i < 60; i++)
        {
            entries.Add(BuildAutoTrackedFilterEntry($"recent-{i}", now.AddMinutes(-i)));
        }
        var oldTagged = BuildAutoTrackedFilterEntry("ancient-tagged", now.AddDays(-30)) with { Tags = ["special"] };
        entries.Add(oldTagged);

        SetState(new FilterLibraryState { Entries = [.. entries], IsLoaded = true });

        var component = Render<FilterLibraryModal>();
        await component.FindAll("[role='tab']")[2].ClickAsync(new MouseEventArgs());

        var puPanel = component.Find("[role='tabpanel'].active");
        var specialChip = puPanel.QuerySelectorAll(".library-tag-filter-chip")
            .Single(c => c.TextContent.Trim() == "special");
        await specialChip.ClickAsync(new MouseEventArgs());

        var rows = component.Find("[role='tabpanel'].active").QuerySelectorAll(".library-entry-row");
        Assert.Single(rows);
        Assert.Contains("ancient-tagged", rows[0].TextContent);
    }

    [Fact]
    public async Task TagFilterChip_PerTabIndependence_SavedSelectionDoesNotAffectFavorites()
    {
        var savedAlpha = BuildSavedFilter("savedAlpha") with { Tags = ["alpha"] };
        var favBeta = BuildSavedFilter("favBeta") with { IsFavorite = true, Tags = ["beta"] };
        SetState(new FilterLibraryState { Entries = [savedAlpha, favBeta], IsLoaded = true });

        var component = Render<FilterLibraryModal>();

        var savedPanel = component.Find("[role='tabpanel'].active");
        var savedAlphaChip = savedPanel.QuerySelectorAll(".library-tag-filter-chip")[0];
        await savedAlphaChip.ClickAsync(new MouseEventArgs());

        await component.FindAll("[role='tab']")[1].ClickAsync(new MouseEventArgs());

        var favPanel = component.Find("[role='tabpanel'].active");
        var favChips = favPanel.QuerySelectorAll(".library-tag-filter-chip");
        foreach (var chip in favChips)
        {
            Assert.Equal("false", chip.GetAttribute("aria-pressed"));
        }
        Assert.Single(favPanel.QuerySelectorAll(".library-entry-row"));
    }

    [Fact]
    public async Task TagFilterChip_SelectedThenCleared_RestoresFullTab()
    {
        var alpha = BuildSavedFilter("alphaEntry") with { Tags = ["alpha"] };
        var beta = BuildSavedFilter("betaEntry") with { Tags = ["beta"] };
        SetState(new FilterLibraryState { Entries = [alpha, beta], IsLoaded = true });

        var component = Render<FilterLibraryModal>();
        var activePanel = component.Find("[role='tabpanel'].active");
        var alphaChip = activePanel.QuerySelectorAll(".library-tag-filter-chip")[0];

        await alphaChip.ClickAsync(new MouseEventArgs());
        await component.Find("[role='tabpanel'].active")
            .QuerySelectorAll(".library-tag-filter-chip")[0]
            .ClickAsync(new MouseEventArgs());

        Assert.Equal(2, component.Find("[role='tabpanel'].active").QuerySelectorAll(".library-entry-row").Length);
    }

    [Fact]
    public async Task TagFilterChip_Selection_NarrowsTabToEntriesCarryingTag()
    {
        var alpha = BuildSavedFilter("alphaEntry") with { Tags = ["alpha"] };
        var beta = BuildSavedFilter("betaEntry") with { Tags = ["beta"] };
        SetState(new FilterLibraryState { Entries = [alpha, beta], IsLoaded = true });

        var component = Render<FilterLibraryModal>();

        Assert.Equal(2, component.FindAll(".library-entry-row").Count);

        var activePanel = component.Find("[role='tabpanel'].active");
        var alphaChip = activePanel.QuerySelectorAll(".library-tag-filter-chip")[0];
        await alphaChip.ClickAsync(new MouseEventArgs());

        var visibleRows = component.Find("[role='tabpanel'].active").QuerySelectorAll(".library-entry-row");
        Assert.Single(visibleRows);
        Assert.Contains("alphaEntry", visibleRows[0].TextContent);
    }

    private static LibraryEntrySavedFilter BuildAutoTrackedFilterEntry(string name, DateTimeOffset? lastUsed = null)
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);

        return new LibraryEntrySavedFilter
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filter = filter,
            Origin = LibraryEntryOrigin.AutoTracked,
            LastUsedUtc = lastUsed ?? DateTimeOffset.UtcNow,
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

    private static LibraryEntrySavedFilter BuildSavedFilter(string name) =>
        BuildFilterEntry(name) with { Origin = LibraryEntryOrigin.UserSaved };

    private void SetState(FilterLibraryState state)
    {
        var stateMock = Substitute.For<IState<FilterLibraryState>>();
        stateMock.Value.Returns(state);
        Services.AddSingleton(stateMock);
    }
}
