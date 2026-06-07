// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.FilterProgress;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.FilterEditor;
using EventLogExpert.UI.FilterPane;
using EventLogExpert.UI.Tests.TestUtils;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Collections.Immutable;
using System.Reflection;

namespace EventLogExpert.UI.Tests.FilterPane;

public sealed class FilterPaneTests : BunitContext
{
    private readonly IAnnouncementService _announcements = Substitute.For<IAnnouncementService>();
    private readonly IFilterLibraryCommands _filterLibraryCommands = Substitute.For<IFilterLibraryCommands>();
    private readonly IFilterPaneCommands _filterPaneCommands = Substitute.For<IFilterPaneCommands>();
    private readonly IState<FilterLibraryState> _libraryStateMock = Substitute.For<IState<FilterLibraryState>>();
    private readonly IState<FilterPaneState> _paneStateMock = Substitute.For<IState<FilterPaneState>>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();

    public FilterPaneTests()
    {
        Services.AddBannerHostDependencies();
        Services.AddMenuMocks();

        Services.AddSingleton(_announcements);
        Services.AddSingleton(_filterLibraryCommands);
        Services.AddSingleton(_filterPaneCommands);
        Services.AddSingleton(_libraryStateMock);
        Services.AddSingleton(_settings);
        Services.AddSingleton(Substitute.For<IAlertDialogService>());
        Services.AddSingleton(Substitute.For<IModalCoordinator>());
        Services.AddSingleton(Substitute.For<IMenuActionService>());

        var paneState = _paneStateMock;
        paneState.Value.Returns(new FilterPaneState());
        Services.AddSingleton(paneState);

        var progressState = Substitute.For<IState<FilterProgressState>>();
        progressState.Value.Returns(new FilterProgressState());
        Services.AddSingleton(progressState);

        var eventLogState = Substitute.For<IState<EventLogState>>();
        eventLogState.Value.Returns(new EventLogState());
        Services.AddSingleton(eventLogState);

        _settings.TimeZoneInfo.Returns(TimeZoneInfo.Utc);

        Services.AddFluxor(options => options.ScanAssemblies(typeof(UI.FilterPane.FilterPane).Assembly));

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void ApplyFilterSetSelection_WhenLoadError_AnnouncesAndDoesNotApply()
    {
        var filterSet = BuildFilterSet("AnyName");
        SetLibraryState(new FilterLibraryState
        {
            IsLoaded = true,
            LoadError = true,
            Entries = ImmutableList<LibraryEntry>.Empty,
        });
        var component = Render<UI.FilterPane.FilterPane>();
        component.Instance.SelectedFilterSetId = filterSet.Id;

        component.Instance.ApplyFilterSetSelection();

        _announcements.Received(1).Announce(FilterPaneAnnouncements.LoadFailedRetryViaModal);
        _filterLibraryCommands.DidNotReceiveWithAnyArgs().ApplyEntry(default);
    }

    [Fact]
    public void ApplyFilterSetSelection_WhenStaleFilterSet_AnnouncesResetsAndDoesNotApply()
    {
        var filterSetA = BuildFilterSet("Alpha");
        var stale = new LibraryEntryId(Guid.NewGuid());
        SetLibraryState(new FilterLibraryState
        {
            IsLoaded = true,
            Entries = ImmutableList.Create<LibraryEntry>(filterSetA),
        });
        var component = Render<UI.FilterPane.FilterPane>();
        component.Instance.SelectedFilterSetId = stale;

        component.Instance.ApplyFilterSetSelection();

        _announcements.Received(1).Announce(FilterPaneAnnouncements.SelectedFilterSetMissing);
        Assert.Equal(filterSetA.Id, component.Instance.SelectedFilterSetId);
        _filterLibraryCommands.DidNotReceiveWithAnyArgs().ApplyEntry(default);
    }

    [Fact]
    public void ApplyFilterSetSelection_WhenStillLoading_AnnouncesAndDoesNotApply()
    {
        var filterSet = BuildFilterSet("AnyName");
        SetLibraryState(new FilterLibraryState
        {
            IsLoaded = false,
            Entries = ImmutableList<LibraryEntry>.Empty,
        });
        var component = Render<UI.FilterPane.FilterPane>();
        component.Instance.SelectedFilterSetId = filterSet.Id;

        component.Instance.ApplyFilterSetSelection();

        _announcements.Received(1).Announce(FilterPaneAnnouncements.LoadingTryAgain);
        _filterLibraryCommands.DidNotReceiveWithAnyArgs().ApplyEntry(default);
    }

    [Fact]
    public void ApplyFilterSetSelection_WhenSuccess_AppliesAndDoesNotAnnounce()
    {
        var filterSet = BuildFilterSet("Picked");
        SetLibraryState(new FilterLibraryState
        {
            IsLoaded = true,
            Entries = ImmutableList.Create<LibraryEntry>(filterSet),
        });
        var component = Render<UI.FilterPane.FilterPane>();
        component.Instance.SelectedFilterSetId = filterSet.Id;

        component.Instance.ApplyFilterSetSelection();

        _filterLibraryCommands.Received(1).ApplyEntry(filterSet.Id);
        _announcements.DidNotReceiveWithAnyArgs().Announce(null!);
    }

    [Fact]
    public void GetRecentDisabledReason_WhenEmpty_ReturnsRecentNoneAvailable()
    {
        SetLibraryState(new FilterLibraryState
        {
            IsLoaded = true,
            Entries = ImmutableList<LibraryEntry>.Empty,
        });
        var component = Render<UI.FilterPane.FilterPane>();

        var reason = component.Instance.GetRecentDisabledReason();

        Assert.Equal(FilterPaneAnnouncements.RecentNoneAvailable, reason);
    }

    [Fact]
    public void GetRecentDisabledReason_WhenHasFavoriteFilter_ReturnsNull()
    {
        SetLibraryState(new FilterLibraryState
        {
            IsLoaded = true,
            Entries = ImmutableList.Create<LibraryEntry>(BuildSavedFilter("Fav", isFavorite: true)),
        });
        var component = Render<UI.FilterPane.FilterPane>();

        var reason = component.Instance.GetRecentDisabledReason();

        Assert.Null(reason);
    }

    [Fact]
    public void GetRecentDisabledReason_WhenHasNonFavoriteRecent_ReturnsNull()
    {
        SetLibraryState(new FilterLibraryState
        {
            IsLoaded = true,
            Entries = ImmutableList.Create<LibraryEntry>(
                BuildSavedFilter("Recent", isFavorite: false, lastUsed: DateTimeOffset.UtcNow)),
        });
        var component = Render<UI.FilterPane.FilterPane>();

        var reason = component.Instance.GetRecentDisabledReason();

        Assert.Null(reason);
    }

    [Theory]
    [InlineData(true, false, true, "Filter library failed to load. Open Filter Library to retry.")]
    [InlineData(false, false, false, "Filter library is still loading. Please try again.")]
    [InlineData(true, true, true, "Filter library failed to load. Open Filter Library to retry.")]
    public void GetRecentDisabledReason_WhenLoadErrorOrLoading_ReturnsContextSpecificMessage(
        bool isLoaded, bool hasEntries, bool loadError, string expectedReason)
    {
        var entries = hasEntries
            ? ImmutableList.Create<LibraryEntry>(BuildSavedFilter("X", isFavorite: true))
            : ImmutableList<LibraryEntry>.Empty;
        SetLibraryState(new FilterLibraryState
        {
            IsLoaded = isLoaded,
            LoadError = loadError,
            Entries = entries,
        });
        var component = Render<UI.FilterPane.FilterPane>();

        var reason = component.Instance.GetRecentDisabledReason();

        Assert.Equal(expectedReason, reason);
    }

    [Fact]
    public void OnRowDisposed_RemovesMatchingRowRef()
    {
        var pane = new UI.FilterPane.FilterPane();
        var rowRefs = (Dictionary<FilterId, FilterRow?>)typeof(UI.FilterPane.FilterPane)
            .GetField("_rowRefs", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(pane)!;
        var row = new FilterRow();
        var id = SavedFilter.TryCreate("Level == 4")!.Id;
        rowRefs[id] = row;

        typeof(UI.FilterPane.FilterPane)
            .GetMethod("OnRowDisposed", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(pane, [id]);

        Assert.DoesNotContain(id, rowRefs.Keys);
    }

    [Fact]
    public void OpenFilterSetPicker_PreSelectsFirstFilterSetCaseInsensitive()
    {
        var filterSetZ = BuildFilterSet("ZebraGroup");
        var filterSetA = BuildFilterSet("alphaGroup");
        SetLibraryState(new FilterLibraryState
        {
            IsLoaded = true,
            Entries = ImmutableList.Create<LibraryEntry>(filterSetZ, filterSetA),
        });
        var component = Render<UI.FilterPane.FilterPane>();

        component.Instance.OpenFilterSetPicker();

        Assert.Equal(filterSetA.Id, component.Instance.SelectedFilterSetId);
    }

    [Fact]
    public void OpenFilterSetPicker_WhenLoadError_AnnouncesAndKeepsClosed()
    {
        SetLibraryState(new FilterLibraryState { IsLoaded = true, LoadError = true });
        var component = Render<UI.FilterPane.FilterPane>();

        component.Instance.OpenFilterSetPicker();

        _announcements.Received(1).Announce(FilterPaneAnnouncements.LoadFailedRetryViaModal);
    }

    [Fact]
    public void OpenFilterSetPicker_WhenNoFilterSets_OpensWithDefaultFilterSetIdAndNoAnnouncement()
    {
        SetLibraryState(new FilterLibraryState
        {
            IsLoaded = true,
            Entries = ImmutableList<LibraryEntry>.Empty,
        });
        var component = Render<UI.FilterPane.FilterPane>();

        component.Instance.OpenFilterSetPicker();

        Assert.True(component.Instance.IsFilterSetPickerVisible);
        Assert.Equal(default(LibraryEntryId), component.Instance.SelectedFilterSetId);
        _announcements.DidNotReceiveWithAnyArgs().Announce(null!);
    }

    [Fact]
    public void OpenFilterSetPicker_WhenStillLoading_AnnouncesAndKeepsClosed()
    {
        SetLibraryState(new FilterLibraryState { IsLoaded = false, LoadError = false });
        var component = Render<UI.FilterPane.FilterPane>();

        component.Instance.OpenFilterSetPicker();

        _announcements.Received(1).Announce(FilterPaneAnnouncements.LoadingTryAgain);
    }

    [Fact]
    public void PruneStaleRowRefs_RemovesNullRefForLiveFilter()
    {
        var filter = SavedFilter.TryCreate("Level == 4")!;
        SetPaneState(new FilterPaneState { Filters = [filter] });

        var pane = new UI.FilterPane.FilterPane();
        typeof(UI.FilterPane.FilterPane)
            .GetProperty("FilterPaneState", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(pane, _paneStateMock);

        var rowRefs = (Dictionary<FilterId, FilterRow?>)typeof(UI.FilterPane.FilterPane)
            .GetField("_rowRefs", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(pane)!;

        // Live filter id, but its @ref was cleared to null: only the value-null check can prune it
        // (the id is still live, and a non-empty filter list skips the clear-all fast path).
        rowRefs[filter.Id] = null;

        typeof(UI.FilterPane.FilterPane)
            .GetMethod("PruneStaleRowRefs", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(pane, null);

        Assert.DoesNotContain(filter.Id, rowRefs.Keys);
    }

    private static LibraryEntryFilterSet BuildFilterSet(string name) =>
        new()
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = ImmutableList<SavedFilter>.Empty,
        };

    private static LibraryEntrySavedFilter BuildSavedFilter(string name, bool isFavorite = false, DateTimeOffset? lastUsed = null)
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);

        return new LibraryEntrySavedFilter
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filter = filter,
            IsFavorite = isFavorite,
            LastUsedUtc = isFavorite ? null : lastUsed,
        };
    }

    private void SetLibraryState(FilterLibraryState state) => _libraryStateMock.Value.Returns(state);

    private void SetPaneState(FilterPaneState state) => _paneStateMock.Value.Returns(state);
}
