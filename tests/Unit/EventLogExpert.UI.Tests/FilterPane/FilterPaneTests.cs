// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using AngleSharp.Dom;
using Bunit;
using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.FilterProgress;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.Runtime.Scenarios;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.Scenarios.Catalog;
using EventLogExpert.UI.FilterEditor;
using EventLogExpert.UI.FilterPane;
using EventLogExpert.UI.Tests.TestUtils;
using Fluxor;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Collections.Immutable;
using System.Reflection;
using Reducers = EventLogExpert.Runtime.EventLog.Reducers;

namespace EventLogExpert.UI.Tests.FilterPane;

public sealed class FilterPaneTests : BunitContext
{
    private readonly IAnnouncementService _announcements = Substitute.For<IAnnouncementService>();
    private readonly IEventLogQueries _eventLogQueries = Substitute.For<IEventLogQueries>();
    private readonly IState<EventLogState> _eventLogStateMock = Substitute.For<IState<EventLogState>>();
    private readonly IFilterLibraryCommands _filterLibraryCommands = Substitute.For<IFilterLibraryCommands>();
    private readonly IFilterPaneCommands _filterPaneCommands = Substitute.For<IFilterPaneCommands>();
    private readonly IState<FilterLibraryState> _libraryStateMock = Substitute.For<IState<FilterLibraryState>>();
    private readonly IStateSelection<EventLogState, ImmutableHashSet<string>> _loadedLogNames =
        Substitute.For<IStateSelection<EventLogState, ImmutableHashSet<string>>>();
    private readonly IStateSelection<EventLogState, int> _openLogCount =
        Substitute.For<IStateSelection<EventLogState, int>>();
    private readonly IState<FilterPaneState> _paneStateMock = Substitute.For<IState<FilterPaneState>>();
    private readonly IScenarioApplyService _scenarioApply = Substitute.For<IScenarioApplyService>();
    private readonly IScenarioQueryService _scenarioQuery = Substitute.For<IScenarioQueryService>();
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
        Services.AddSingleton(Substitute.For<IScenarioAuthoringService>());
        Services.AddSingleton(Substitute.For<IClipboardService>());
        Services.AddSingleton(Substitute.For<IFilePickerService>());
        Services.AddSingleton(_scenarioApply);
        Services.AddSingleton(_scenarioQuery);
        Services.AddSingleton(new ScenarioAuthoringOptions(false));

        var paneState = _paneStateMock;
        paneState.Value.Returns(new FilterPaneState());
        Services.AddSingleton(paneState);

        var progressState = Substitute.For<IState<FilterProgressState>>();
        progressState.Value.Returns(new FilterProgressState());
        Services.AddSingleton(progressState);

        _eventLogStateMock.Value.Returns(new EventLogState());
        Services.AddSingleton(_eventLogStateMock);
        Services.AddSingleton(_eventLogQueries);

        _loadedLogNames.Value.Returns(ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase));
        _openLogCount.Value.Returns(0);
        Services.AddSingleton(_loadedLogNames);
        Services.AddSingleton(_openLogCount);

        _settings.TimeZoneInfo.Returns(TimeZoneInfo.Utc);

        Services.AddFluxor(options => options.ScanAssemblies(typeof(UI.FilterPane.FilterPane).Assembly));

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void AddDateFilter_PreFillsModelFromEventLogQueriesRange()
    {
        var timeZone = TimeZoneInfo.CreateCustomTimeZone("F1Plus05", TimeSpan.FromHours(5), "F1Plus05", "F1Plus05");
        _settings.TimeZoneInfo.Returns(timeZone);
        var after = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var before = new DateTime(2020, 1, 2, 8, 0, 0, DateTimeKind.Utc);
        _eventLogQueries.GetEventDateRange(Arg.Any<DateTime>()).Returns((after, before));
        var component = Render<UI.FilterPane.FilterPane>();

        typeof(UI.FilterPane.FilterPane)
            .GetMethod("AddDateFilter", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(component.Instance, null);

        var model = (DateFilter)typeof(UI.FilterPane.FilterPane)
            .GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(component.Instance)!;
        _eventLogQueries.Received(1).GetEventDateRange(Arg.Any<DateTime>());
        Assert.Equal(TimeZoneInfo.ConvertTimeFromUtc(after, timeZone), model.After!.Value);
        Assert.Equal(TimeZoneInfo.ConvertTimeFromUtc(before, timeZone), model.Before!.Value);
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
    public void ApplyScenarioButton_WhenLogsLoaded_IsShownAsExpander()
    {
        SetLibraryState(new FilterLibraryState { IsLoaded = true, Entries = ImmutableList<LibraryEntry>.Empty });
        SetLoadedLogNames("System");
        SetOpenLogCount(1);

        var component = Render<UI.FilterPane.FilterPane>();
        var button = FindApplyScenarioButton(component);

        Assert.NotNull(button);
        Assert.Null(button!.GetAttribute("aria-haspopup"));
        Assert.Equal("false", button.GetAttribute("aria-expanded"));
        Assert.Equal("scenario-picker", button.GetAttribute("aria-controls"));
    }

    [Fact]
    public void ApplyScenarioButton_WhenNoLogsLoaded_IsHidden()
    {
        SetLibraryState(new FilterLibraryState { IsLoaded = true, Entries = ImmutableList<LibraryEntry>.Empty });

        var component = Render<UI.FilterPane.FilterPane>();

        Assert.Null(FindApplyScenarioButton(component));
    }

    [Fact]
    public void ApplyScenarioSelection_AfterCategoryChange_AppliesScenarioFromNewCategory()
    {
        var sys = Scenario("sys", ScenarioGroup.SystemHealth);
        var sec = Scenario("sec", ScenarioGroup.Security);
        SetLibraryState(new FilterLibraryState { IsLoaded = true, Entries = ImmutableList<LibraryEntry>.Empty });
        SetLoadedLogNames("System");
        SetOpenLogCount(1);
        _scenarioQuery.GetInAppScenarios(Arg.Any<IReadOnlyCollection<string>>()).Returns([sys, sec]);

        var component = Render<UI.FilterPane.FilterPane>();
        component.Instance.OpenScenarioPicker();
        component.Instance.OnScenarioGroupChanged(ScenarioGroup.Security);

        component.Instance.ApplyScenarioSelection();

        _scenarioApply.Received(1).ApplyInApp(sec, false);
    }

    [Fact]
    public void ApplyScenarioSelection_WhenNoMatches_AnnouncesAndDoesNotApply()
    {
        SetLibraryState(new FilterLibraryState { IsLoaded = true, Entries = ImmutableList<LibraryEntry>.Empty });
        SetLoadedLogNames("System");
        SetOpenLogCount(1);
        _scenarioQuery.GetInAppScenarios(Arg.Any<IReadOnlyCollection<string>>()).Returns([]);

        var component = Render<UI.FilterPane.FilterPane>();
        component.Instance.OpenScenarioPicker();

        component.Instance.ApplyScenarioSelection();

        _announcements.Received(1).Announce(FilterPaneAnnouncements.SelectedScenarioMissing);
        _scenarioApply.DidNotReceiveWithAnyArgs().ApplyInApp(null!, false);
    }

    [Fact]
    public void ApplyScenarioSelection_WhenSelectionStale_AppliesFirstVisibleScenario()
    {
        var match = Scenario("sys", ScenarioGroup.SystemHealth);
        SetLibraryState(new FilterLibraryState { IsLoaded = true, Entries = ImmutableList<LibraryEntry>.Empty });
        SetLoadedLogNames("System");
        SetOpenLogCount(1);
        _scenarioQuery.GetInAppScenarios(Arg.Any<IReadOnlyCollection<string>>()).Returns([match]);

        var component = Render<UI.FilterPane.FilterPane>();
        component.Instance.OpenScenarioPicker();
        component.Instance.SelectedScenarioId = "no-such-scenario";

        component.Instance.ApplyScenarioSelection();

        _scenarioApply.Received(1).ApplyInApp(match, false);
    }

    [Fact]
    public void AvailableTagsForSets_ReturnsDistinctSortedUnion()
    {
        var a = BuildFilterSet("A", ["zebra", "alpha"]);
        var b = BuildFilterSet("B", ["alpha", "mid"]);

        var tags = UI.FilterPane.FilterPane.AvailableTagsForSets([a, b]);

        Assert.Equal(new[] { "alpha", "mid", "zebra" }, tags.ToArray());
    }

    [Fact]
    public async Task CancelScenarioPicker_ResetsSelectedScenarioId()
    {
        var scenario = Scenario("sys", ScenarioGroup.SystemHealth);
        SetLibraryState(new FilterLibraryState { IsLoaded = true, Entries = ImmutableList<LibraryEntry>.Empty });
        SetLoadedLogNames("System");
        SetOpenLogCount(1);
        _scenarioQuery.GetInAppScenarios(Arg.Any<IReadOnlyCollection<string>>()).Returns([scenario]);

        var component = Render<UI.FilterPane.FilterPane>();
        await FindApplyScenarioButton(component)!.ClickAsync(new MouseEventArgs());
        Assert.Equal(scenario.Id, component.Instance.SelectedScenarioId);

        await component.Find("#scenario-picker button.button-red").ClickAsync(new MouseEventArgs());

        Assert.False(component.Instance.IsScenarioPickerVisible);
        Assert.Null(component.Instance.SelectedScenarioGroup);
        Assert.Null(component.Instance.SelectedScenarioId);
    }

    [Fact]
    public async Task CopyScenario_ExportsOnlyEnabledRows()
    {
        Services.AddSingleton(new ScenarioAuthoringOptions(true));
        var authoring = Services.GetRequiredService<IScenarioAuthoringService>();
        authoring.ExportRows(Arg.Any<IReadOnlyList<SavedFilter>>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(new ScenarioExportResult("{}", ImmutableList<string>.Empty, EmittedRowCount: 1));

        var enabled = SavedFilter.TryCreate("Level == 4")! with { IsEnabled = true };
        var disabled = SavedFilter.TryCreate("Level == 2")! with { IsEnabled = false };
        SetLibraryState(new FilterLibraryState { IsLoaded = true, Entries = ImmutableList<LibraryEntry>.Empty });
        SetPaneState(new FilterPaneState { Filters = [enabled, disabled] });

        var component = Render<UI.FilterPane.FilterPane>();
        var copyButton = component.FindAll("button")
            .First(button => button.GetAttribute("aria-label") == "Copy scenario JSON");

        await copyButton.ClickAsync(new MouseEventArgs());

        authoring.Received(1).ExportRows(
            Arg.Is<IReadOnlyList<SavedFilter>>(rows => rows.Count == 1 && rows[0].IsEnabled),
            Arg.Any<IReadOnlyList<string>>());
    }

    [Fact]
    public void EditButton_OnActiveFilterRow_EntersEditMode()
    {
        Services.AddSingleton(new ScenarioAuthoringOptions(true));
        SetLibraryState(new FilterLibraryState { IsLoaded = true, Entries = ImmutableList<LibraryEntry>.Empty });
        SetPaneState(new FilterPaneState { Filters = [SavedFilter.TryCreate("Level == 4")!] });

        var component = Render<UI.FilterPane.FilterPane>();

        // The per-row scenario-copy button must be present for this regression (it sits alongside Edit in the row).
        Assert.Contains(
            component.FindAll("button"),
            b => b.GetAttribute("aria-label")?.Contains("scenario JSON") == true);

        var editButton = component.FindAll("button")
            .FirstOrDefault(b => b.GetAttribute("aria-label")?.StartsWith("Edit ", StringComparison.Ordinal) == true);
        Assert.NotNull(editButton);

        editButton!.Click();

        // Entering edit mode replaces the saved-row actions (including the Edit button) with the edit panel.
        Assert.DoesNotContain(
            component.FindAll("button"),
            b => b.GetAttribute("aria-label")?.StartsWith("Edit ", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void FilterSetReplaceButton_DisabledGatingMirrorsSelection()
    {
        var filterSet = BuildFilterSet("Picked");
        SetLibraryState(new FilterLibraryState
        {
            IsLoaded = true,
            Entries = ImmutableList.Create<LibraryEntry>(filterSet),
        });
        var component = Render<UI.FilterPane.FilterPane>();

        component.Instance.OpenFilterSetPicker();
        component.Render();

        var replace = component.Find("button[aria-label='Replace filters with selected filter set']");
        Assert.False(replace.HasAttribute("disabled"));

        component.Instance.SelectedFilterSetId = default;
        component.Render();

        Assert.True(component.Find("button[aria-label='Replace filters with selected filter set']").HasAttribute("disabled"));
    }

    [Fact]
    public void FilterSetsByTags_AllSemantics_NarrowsToSetsWithEveryTag()
    {
        var both = BuildFilterSet("Both", ["x", "y"]);
        var onlyX = BuildFilterSet("OnlyX", ["x"]);

        var result = UI.FilterPane.FilterPane.FilterSetsByTags([both, onlyX], ["x", "y"], default);

        Assert.Single(result);
        Assert.Equal("Both", result[0].Name);
    }

    [Fact]
    public void FilterSetsByTags_NoSelectedTags_ReturnsAllSetsOrderedByName()
    {
        var zebra = BuildFilterSet("Zebra", ["x"]);
        var alpha = BuildFilterSet("Alpha", ["y"]);

        var result = UI.FilterPane.FilterPane.FilterSetsByTags([zebra, alpha], [], default);

        Assert.Equal(new[] { "Alpha", "Zebra" }, result.Select(s => s.Name).ToArray());
    }

    [Fact]
    public void FilterSetsByTags_PreservesCurrentSelectionEvenWhenExcluded()
    {
        var tagged = BuildFilterSet("Tagged", ["x"]);
        var current = BuildFilterSet("Current", ["other"]);

        var result = UI.FilterPane.FilterPane.FilterSetsByTags([tagged, current], ["x"], current.Id);

        Assert.Contains(result, s => s.Name == "Current");
        Assert.Contains(result, s => s.Name == "Tagged");
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
    public void OnScenarioGroupChanged_ResetsScenarioToFirstOfNewCategory()
    {
        var sys = Scenario("sys", ScenarioGroup.SystemHealth);
        var sec1 = Scenario("sec1", ScenarioGroup.Security);
        var sec2 = Scenario("sec2", ScenarioGroup.Security);
        SetLibraryState(new FilterLibraryState { IsLoaded = true, Entries = ImmutableList<LibraryEntry>.Empty });
        SetLoadedLogNames("System");
        SetOpenLogCount(1);
        _scenarioQuery.GetInAppScenarios(Arg.Any<IReadOnlyCollection<string>>()).Returns([sys, sec1, sec2]);

        var component = Render<UI.FilterPane.FilterPane>();
        component.Instance.OpenScenarioPicker();

        component.Instance.OnScenarioGroupChanged(ScenarioGroup.Security);

        Assert.Equal(ScenarioGroup.Security, component.Instance.SelectedScenarioGroup);
        Assert.Equal("sec1", component.Instance.SelectedScenarioId);
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
    public async Task OpenScenarioPicker_FileLog_SurfacesScenariosFromEventLogName()
    {
        SetLibraryState(new FilterLibraryState { IsLoaded = true, Entries = ImmutableList<LibraryEntry>.Empty });
        SetLoadedLogNames("Security");
        SetOpenLogCount(1);

        IReadOnlyCollection<string>? capturedNames = null;
        _scenarioQuery.GetInAppScenarios(Arg.Do<IReadOnlyCollection<string>>(names => capturedNames = names))
            .Returns([Scenario("sec", ScenarioGroup.Security)]);

        var component = Render<UI.FilterPane.FilterPane>();
        await FindApplyScenarioButton(component)!.ClickAsync(new MouseEventArgs());

        Assert.NotNull(capturedNames);
        Assert.Contains("Security", capturedNames!);
        Assert.Contains(component.FindAll(".filter-set-option-name"), option => option.TextContent == "sec");
    }

    [Fact]
    public async Task OpenScenarioPicker_ListsFirstCategoryScenariosInDeclarationOrder()
    {
        SetLibraryState(new FilterLibraryState { IsLoaded = true, Entries = ImmutableList<LibraryEntry>.Empty });
        SetLoadedLogNames("System");
        SetOpenLogCount(1);
        _scenarioQuery.GetInAppScenarios(Arg.Any<IReadOnlyCollection<string>>())
            .Returns(
            [
                Scenario("alpha", ScenarioGroup.Security),
                Scenario("bravo", ScenarioGroup.SystemHealth),
                Scenario("charlie", ScenarioGroup.Security),
            ]);

        var component = Render<UI.FilterPane.FilterPane>();
        await FindApplyScenarioButton(component)!.ClickAsync(new MouseEventArgs());

        var optionNames = component.FindAll(".filter-set-option-name").Select(option => option.TextContent).ToArray();
        Assert.Equal(["bravo"], optionNames);
        Assert.Equal("true", FindApplyScenarioButton(component)!.GetAttribute("aria-expanded"));
    }

    [Fact]
    public async Task OpenScenarioPicker_MultipleCategories_RendersCategoryDropdown()
    {
        SetLibraryState(new FilterLibraryState { IsLoaded = true, Entries = ImmutableList<LibraryEntry>.Empty });
        SetLoadedLogNames("System");
        SetOpenLogCount(1);
        _scenarioQuery.GetInAppScenarios(Arg.Any<IReadOnlyCollection<string>>())
            .Returns([Scenario("sys", ScenarioGroup.SystemHealth), Scenario("sec", ScenarioGroup.Security)]);

        var component = Render<UI.FilterPane.FilterPane>();
        await FindApplyScenarioButton(component)!.ClickAsync(new MouseEventArgs());

        Assert.Contains("Category:", component.Find("#scenario-picker").TextContent);
    }

    [Fact]
    public void OpenScenarioPicker_PreselectsFirstMatch()
    {
        var first = Scenario("first", ScenarioGroup.SystemHealth);
        SetLibraryState(new FilterLibraryState { IsLoaded = true, Entries = ImmutableList<LibraryEntry>.Empty });
        SetLoadedLogNames("System");
        SetOpenLogCount(1);
        _scenarioQuery.GetInAppScenarios(Arg.Any<IReadOnlyCollection<string>>())
            .Returns([first, Scenario("second", ScenarioGroup.SystemHealth)]);

        var component = Render<UI.FilterPane.FilterPane>();
        component.Instance.OpenScenarioPicker();

        Assert.Equal(ScenarioGroup.SystemHealth, component.Instance.SelectedScenarioGroup);
        Assert.Equal(first.Id, component.Instance.SelectedScenarioId);
    }

    [Fact]
    public async Task OpenScenarioPicker_SingleCategory_OmitsCategoryDropdown()
    {
        SetLibraryState(new FilterLibraryState { IsLoaded = true, Entries = ImmutableList<LibraryEntry>.Empty });
        SetLoadedLogNames("System");
        SetOpenLogCount(1);
        _scenarioQuery.GetInAppScenarios(Arg.Any<IReadOnlyCollection<string>>())
            .Returns([Scenario("one", ScenarioGroup.SystemHealth), Scenario("two", ScenarioGroup.SystemHealth)]);

        var component = Render<UI.FilterPane.FilterPane>();
        await FindApplyScenarioButton(component)!.ClickAsync(new MouseEventArgs());

        Assert.DoesNotContain("Category:", component.Find("#scenario-picker").TextContent);
    }

    [Fact]
    public async Task OpenScenarioPicker_WhenNoMatches_ShowsEmptyStateStatus()
    {
        SetLibraryState(new FilterLibraryState { IsLoaded = true, Entries = ImmutableList<LibraryEntry>.Empty });
        SetLoadedLogNames("System");
        SetOpenLogCount(1);
        _scenarioQuery.GetInAppScenarios(Arg.Any<IReadOnlyCollection<string>>()).Returns([]);

        var component = Render<UI.FilterPane.FilterPane>();
        await FindApplyScenarioButton(component)!.ClickAsync(new MouseEventArgs());

        var status = component.Find("[role='status']");
        Assert.Equal("No scenarios match the loaded logs.", status.TextContent);
    }

    [Fact]
    public void PerRowScenarioCopy_WhenAuthoringDisabled_RendersNoButton()
    {
        SetLibraryState(new FilterLibraryState { IsLoaded = true, Entries = ImmutableList<LibraryEntry>.Empty });
        SetPaneState(new FilterPaneState { Filters = [SavedFilter.TryCreate("Level == 4")!] });

        var component = Render<UI.FilterPane.FilterPane>();

        Assert.DoesNotContain(
            component.FindAll("button"),
            button => button.GetAttribute("aria-label")?.Contains("scenario JSON") == true);
    }

    [Fact]
    public void PerRowScenarioCopy_WhenAuthoringEnabled_RendersButtonOnFilterRow()
    {
        Services.AddSingleton(new ScenarioAuthoringOptions(true));
        SetLibraryState(new FilterLibraryState { IsLoaded = true, Entries = ImmutableList<LibraryEntry>.Empty });
        SetPaneState(new FilterPaneState { Filters = [SavedFilter.TryCreate("Level == 4")!] });

        var component = Render<UI.FilterPane.FilterPane>();

        Assert.Contains(
            component.FindAll("button"),
            button => button.GetAttribute("aria-label")?.Contains("scenario JSON") == true);
    }

    [Fact]
    public void PruneStaleFilterSetTags_RemovesTagsNoLongerAvailable()
    {
        var tagged = BuildFilterSet("Set", ["keep"]);
        SetLibraryState(new FilterLibraryState
        {
            IsLoaded = true,
            Entries = ImmutableList.Create<LibraryEntry>(tagged),
        });
        var component = Render<UI.FilterPane.FilterPane>();

        var tagsField = (List<string>)typeof(UI.FilterPane.FilterPane)
            .GetField("_filterSetTags", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(component.Instance)!;
        tagsField.Add("keep");
        tagsField.Add("gone");

        component.Render();

        Assert.Contains("keep", tagsField);
        Assert.DoesNotContain("gone", tagsField);
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

    [Fact]
    public async Task RenderIsolation_WhenFileLoadAddsName_ReRendersAndUpdatesScenarioGroups()
    {
        var initial = Reducers.ReduceOpenLog(
            new EventLogState(), new OpenLogAction(@"C:\F.evtx", LogPathType.File));
        var fLog = new EventLogData(@"C:\F.evtx", LogPathType.File) { Id = initial.OpenLogs[@"C:\F.evtx"].Id };
        initial = Reducers.ReduceLoadEvents(
            initial, new LoadEventsAction(fLog, [EventWithName("Security")]));

        SetLibraryState(new FilterLibraryState { IsLoaded = true, Entries = ImmutableList<LibraryEntry>.Empty });

        IReadOnlyCollection<string>? capturedNames = null;
        _scenarioQuery.GetInAppScenarios(Arg.Do<IReadOnlyCollection<string>>(names => capturedNames = names))
            .Returns([Scenario("sec", ScenarioGroup.Security)]);

        var feature = WireRealSelections(initial);
        var component = Render<UI.FilterPane.FilterPane>();
        await FindApplyScenarioButton(component)!.ClickAsync(new MouseEventArgs());

        Assert.NotNull(capturedNames);
        Assert.Contains("Security", capturedNames!);
        var renderCountBefore = component.RenderCount;

        var next = Reducers.ReduceLoadEventsPartial(
            initial, new LoadEventsPartialAction(fLog, [EventWithName("Application")]));

        await component.InvokeAsync(() => feature.Publish(next));

        Assert.True(component.RenderCount > renderCountBefore);
        Assert.Contains("Application", capturedNames!);
    }

    [Fact]
    public async Task RenderIsolation_WhenNamesByLogChangesButLoadedNamesUnionUnchanged_DoesNotReRender()
    {
        var initial = Reducers.ReduceOpenLog(
            new EventLogState(), new OpenLogAction(@"C:\F.evtx", LogPathType.File));
        initial = Reducers.ReduceOpenLog(
            initial, new OpenLogAction(@"C:\G.evtx", LogPathType.File));
        var fLog = new EventLogData(@"C:\F.evtx", LogPathType.File) { Id = initial.OpenLogs[@"C:\F.evtx"].Id };
        var gLog = new EventLogData(@"C:\G.evtx", LogPathType.File) { Id = initial.OpenLogs[@"C:\G.evtx"].Id };
        initial = Reducers.ReduceLoadEvents(
            initial, new LoadEventsAction(fLog, [EventWithName("A"), EventWithName("B")]));
        initial = Reducers.ReduceLoadEvents(
            initial, new LoadEventsAction(gLog, [EventWithName("B")]));

        var feature = WireRealSelections(initial);
        var component = Render<UI.FilterPane.FilterPane>();
        var renderCountBefore = component.RenderCount;

        var next = Reducers.ReduceLoadEvents(
            initial, new LoadEventsAction(fLog, [EventWithName("A")]));

        Assert.Same(initial.LoadedLogNames, next.LoadedLogNames);
        await component.InvokeAsync(() => feature.Publish(next));

        Assert.Equal(renderCountBefore, component.RenderCount);
    }

    [Fact]
    public async Task RenderIsolation_WhenOpenLogCountChanges_ReRenders()
    {
        var initial = Reducers.ReduceOpenLog(
            new EventLogState(), new OpenLogAction("System", LogPathType.Channel));

        var feature = WireRealSelections(initial);
        var component = Render<UI.FilterPane.FilterPane>();
        var renderCountBefore = component.RenderCount;

        var closed = Reducers.ReduceCloseLog(
            initial, new CloseLogAction(initial.OpenLogs["System"].Id, "System"));

        await component.InvokeAsync(() => feature.Publish(closed));

        Assert.True(component.RenderCount > renderCountBefore);
    }

    [Fact]
    public void ReplaceFilterSetSelection_WhenLoadError_AnnouncesAndDoesNotReplace()
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

        component.Instance.ReplaceFilterSetSelection();

        _announcements.Received(1).Announce(FilterPaneAnnouncements.LoadFailedRetryViaModal);
        _filterLibraryCommands.DidNotReceiveWithAnyArgs().ReplaceWithEntry(default);
    }

    [Fact]
    public void ReplaceFilterSetSelection_WhenStillLoading_AnnouncesAndDoesNotReplace()
    {
        var filterSet = BuildFilterSet("AnyName");
        SetLibraryState(new FilterLibraryState
        {
            IsLoaded = false,
            Entries = ImmutableList<LibraryEntry>.Empty,
        });
        var component = Render<UI.FilterPane.FilterPane>();
        component.Instance.SelectedFilterSetId = filterSet.Id;

        component.Instance.ReplaceFilterSetSelection();

        _announcements.Received(1).Announce(FilterPaneAnnouncements.LoadingTryAgain);
        _filterLibraryCommands.DidNotReceiveWithAnyArgs().ReplaceWithEntry(default);
    }

    [Fact]
    public void ReplaceFilterSetSelection_WhenSuccess_ReplacesAndDoesNotAnnounce()
    {
        var filterSet = BuildFilterSet("Picked");
        SetLibraryState(new FilterLibraryState
        {
            IsLoaded = true,
            Entries = ImmutableList.Create<LibraryEntry>(filterSet),
        });
        var component = Render<UI.FilterPane.FilterPane>();
        component.Instance.SelectedFilterSetId = filterSet.Id;

        component.Instance.ReplaceFilterSetSelection();

        _filterLibraryCommands.Received(1).ReplaceWithEntry(filterSet.Id);
        _announcements.DidNotReceiveWithAnyArgs().Announce(null!);
    }

    [Fact]
    public void SaveAndClearButtons_WhenNoFilters_AreDisabled()
    {
        var component = Render<UI.FilterPane.FilterPane>();

        Assert.True(component.Find("button[aria-label='Save as Filter Set']").HasAttribute("disabled"));
        Assert.True(component.Find("button[aria-label='Clear All Filters']").HasAttribute("disabled"));
    }

    [Fact]
    public async Task SaveScenario_WhenFileWriteFails_ShowsExportFailedAlert()
    {
        Services.AddSingleton(new ScenarioAuthoringOptions(true));
        Services.GetRequiredService<IScenarioAuthoringService>()
            .ExportRows(Arg.Any<IReadOnlyList<SavedFilter>>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(new ScenarioExportResult("{}", ImmutableList<string>.Empty, EmittedRowCount: 1));

        var missingDirectoryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "scenario.json");
        Services.GetRequiredService<IFilePickerService>()
            .PickSaveAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>())
            .Returns(missingDirectoryPath);

        var alertDialog = Services.GetRequiredService<IAlertDialogService>();
        SetLibraryState(new FilterLibraryState { IsLoaded = true, Entries = ImmutableList<LibraryEntry>.Empty });
        SetPaneState(new FilterPaneState { Filters = [SavedFilter.TryCreate("Level == 4")! with { IsEnabled = true }] });

        var component = Render<UI.FilterPane.FilterPane>();
        var saveButton = component.FindAll("button")
            .First(button => button.GetAttribute("aria-label") == "Save scenario JSON");

        await saveButton.ClickAsync(new MouseEventArgs());

        await alertDialog.Received(1).ShowAlert("Export failed", Arg.Any<string>(), "OK");
    }

    [Fact]
    public void ScenarioApplySelection_InvokesApplyInAppMergeAndClosesPicker()
    {
        var scenario = Scenario("sys", ScenarioGroup.SystemHealth);
        SetLibraryState(new FilterLibraryState { IsLoaded = true, Entries = ImmutableList<LibraryEntry>.Empty });
        SetLoadedLogNames("System");
        SetOpenLogCount(1);
        _scenarioQuery.GetInAppScenarios(Arg.Any<IReadOnlyCollection<string>>()).Returns([scenario]);

        var component = Render<UI.FilterPane.FilterPane>();
        component.Instance.OpenScenarioPicker();
        component.Instance.ApplyScenarioSelection();

        _scenarioApply.Received(1).ApplyInApp(scenario, false);
        Assert.False(component.Instance.IsScenarioPickerVisible);
    }

    [Fact]
    public void ScenarioButtons_WhenAllRowsDisabled_AreDisabled()
    {
        Services.AddSingleton(new ScenarioAuthoringOptions(true));
        SetLibraryState(new FilterLibraryState { IsLoaded = true, Entries = ImmutableList<LibraryEntry>.Empty });
        SetPaneState(new FilterPaneState { Filters = [SavedFilter.TryCreate("Level == 4")! with { IsEnabled = false }] });

        var component = Render<UI.FilterPane.FilterPane>();

        Assert.True(component.Find("button[aria-label='Copy scenario JSON']").HasAttribute("disabled"));
        Assert.True(component.Find("button[aria-label='Save scenario JSON']").HasAttribute("disabled"));
    }

    [Fact]
    public void ScenarioReplaceSelection_InvokesApplyInAppReplace()
    {
        var scenario = Scenario("sys", ScenarioGroup.SystemHealth);
        SetLibraryState(new FilterLibraryState { IsLoaded = true, Entries = ImmutableList<LibraryEntry>.Empty });
        SetLoadedLogNames("System");
        SetOpenLogCount(1);
        _scenarioQuery.GetInAppScenarios(Arg.Any<IReadOnlyCollection<string>>()).Returns([scenario]);

        var component = Render<UI.FilterPane.FilterPane>();
        component.Instance.OpenScenarioPicker();
        component.Instance.ReplaceScenarioSelection();

        _scenarioApply.Received(1).ApplyInApp(scenario, true);
    }

    [Fact]
    public void SetQuickDateRange_PopulatesEditFieldsWithoutApplyingFilter()
    {
        var component = Render<UI.FilterPane.FilterPane>();
        component.Instance.EditDateFilter();
        component.Render();

        component.Instance.SetQuickDateRange(new DateFilter
        {
            After = new DateTime(2024, 6, 8, 12, 0, 0, DateTimeKind.Utc),
            Before = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc)
        });
        component.Render();

        Assert.Contains("2024-06-08", component.Find("input[aria-label='After']").GetAttribute("value"));
        Assert.Contains("2024-06-15", component.Find("input[aria-label='Before']").GetAttribute("value"));
        _filterPaneCommands.DidNotReceiveWithAnyArgs().SetFilterDateRange(null);
    }

    private static LibraryEntryFilterSet BuildFilterSet(string name, ImmutableList<string>? tags = null) =>
        new()
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = ImmutableList<SavedFilter>.Empty,
            Tags = tags ?? [],
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

    private static ResolvedEvent EventWithName(string logName) =>
        new("file", LogPathType.File) { LogName = logName };

    private static IElement? FindApplyScenarioButton(IRenderedComponent<UI.FilterPane.FilterPane> component) =>
        component.FindAll("button").FirstOrDefault(button => button.TextContent.Contains("Apply Scenario"));

    private static ScenarioDefinition Scenario(string id, ScenarioGroup group) =>
        new()
        {
            Id = id,
            Name = id,
            Purpose = $"Purpose {id}",
            Group = group,
            Channels = ["System"],
            Filters = [],
        };

    private void SetLibraryState(FilterLibraryState state) => _libraryStateMock.Value.Returns(state);

    private void SetLoadedLogNames(params string[] names) =>
        _loadedLogNames.Value.Returns(ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, names));

    private void SetOpenLogCount(int count) => _openLogCount.Value.Returns(count);

    private void SetPaneState(FilterPaneState state) => _paneStateMock.Value.Returns(state);

    private TestEventLogFeature WireRealSelections(EventLogState initial)
    {
        var feature = new TestEventLogFeature(initial);
        Services.AddSingleton<IState<EventLogState>>(new State<EventLogState>(feature));
        Services.AddSingleton<IStateSelection<EventLogState, ImmutableHashSet<string>>>(
            new StateSelection<EventLogState, ImmutableHashSet<string>>(feature));
        Services.AddSingleton<IStateSelection<EventLogState, int>>(
            new StateSelection<EventLogState, int>(feature));

        return feature;
    }

    private sealed class TestEventLogFeature : Feature<EventLogState>
    {
        private readonly EventLogState _initial;

        public TestEventLogFeature(EventLogState initial)
        {
            _initial = initial;
            MaximumStateChangedNotificationsPerSecond = 0;
            State = initial;
        }

        public override string GetName() => "EventLog";

        public void Publish(EventLogState state) => State = state;

        protected override EventLogState GetInitialState() => _initial;
    }
}
