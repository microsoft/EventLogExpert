// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Localization;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Scenarios;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.Scenarios.Catalog;
using EventLogExpert.UI.Tests.TestUtils;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;
using System.Collections.Immutable;
using System.Reflection;

namespace EventLogExpert.UI.Tests.FilterPane;

public sealed class FilterPaneLocalizerWiringTests : BunitContext
{
    private readonly IActiveFiltersSource _activeFilters = Substitute.For<IActiveFiltersSource>();
    private readonly IAlertDialogService _alertDialog = Substitute.For<IAlertDialogService>();
    private readonly IAnnouncementService _announcements = Substitute.For<IAnnouncementService>();
    private readonly IClearAllFiltersNotifier _clearAllFiltersNotifier = Substitute.For<IClearAllFiltersNotifier>();
    private readonly IClipboardService _clipboard = Substitute.For<IClipboardService>();
    private readonly IEventLogQueries _eventLogQueries = Substitute.For<IEventLogQueries>();
    private readonly IFilePickerService _filePicker = Substitute.For<IFilePickerService>();
    private readonly IFilterLibraryCommands _filterLibraryCommands = Substitute.For<IFilterLibraryCommands>();
    private readonly IFilterPaneCommands _filterPaneCommands = Substitute.For<IFilterPaneCommands>();
    private readonly IFilterPromotedNotifier _filterPromotedNotifier = Substitute.For<IFilterPromotedNotifier>();
    private readonly IFilteredDateRangeSource _filteredDateRange = Substitute.For<IFilteredDateRangeSource>();
    private readonly ILibraryEntriesSource _libraryEntries = Substitute.For<ILibraryEntriesSource>();
    private readonly ILibraryLoadStatusSource _libraryLoadStatus = Substitute.For<ILibraryLoadStatusSource>();
    private readonly ILoadedLogNamesSource _loadedLogNames = Substitute.For<ILoadedLogNamesSource>();
    private readonly IMenuActionService _menuActions = Substitute.For<IMenuActionService>();
    private readonly IOpenLogsPresenceSource _openLogsPresence = Substitute.For<IOpenLogsPresenceSource>();
    private readonly IScenarioApplyService _scenarioApply = Substitute.For<IScenarioApplyService>();
    private readonly IScenarioAuthoringService _scenarioAuthoring = Substitute.For<IScenarioAuthoringService>();
    private readonly IScenarioQueryService _scenarioQuery = Substitute.For<IScenarioQueryService>();
    private readonly ISetFilterDateRangeSucceededNotifier _setFilterDateRangeSucceededNotifier = Substitute.For<ISetFilterDateRangeSucceededNotifier>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private DateFilter? _dateFilter;

    private ImmutableList<SavedFilter> _filters = [];
    private ImmutableList<LibraryEntry> _library = [];
    private LibraryLoadStatus _loadStatus = new(IsLoaded: true, LoadError: false);
    private ImmutableHashSet<string> _loadedNames = ImmutableHashSet<string>.Empty;

    public FilterPaneLocalizerWiringTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddBannerHostDependencies();
        Services.AddMenuMocks();
        Services.AddSingleton<IStringLocalizer<SharedResource>>(new MarkerLocalizer());
        Services.AddSingleton(_activeFilters);
        Services.AddSingleton(_alertDialog);
        Services.AddSingleton(_announcements);
        Services.AddSingleton(_clearAllFiltersNotifier);
        Services.AddSingleton(_clipboard);
        Services.AddSingleton(_eventLogQueries);
        Services.AddSingleton(_filterLibraryCommands);
        Services.AddSingleton(_filterPaneCommands);
        Services.AddSingleton(_filterPromotedNotifier);
        Services.AddSingleton(_filteredDateRange);
        Services.AddSingleton(_filePicker);
        Services.AddSingleton(_libraryEntries);
        Services.AddSingleton(_libraryLoadStatus);
        Services.AddSingleton(_loadedLogNames);
        Services.AddSingleton(_menuActions);
        Services.AddSingleton(_openLogsPresence);
        Services.AddSingleton(_scenarioApply);
        Services.AddSingleton(_scenarioAuthoring);
        Services.AddSingleton(_scenarioQuery);
        Services.AddSingleton(_setFilterDateRangeSucceededNotifier);
        Services.AddSingleton(_settings);
        Services.AddSingleton(new ScenarioAuthoringOptions(true));
        Services.AddFluxor(options => options.ScanAssemblies(typeof(UI.FilterPane.FilterPane).Assembly));

        _activeFilters.Current.Returns(_ => _filters);
        _filteredDateRange.Current.Returns(_ => _dateFilter);
        _libraryEntries.Current.Returns(_ => _library);
        _libraryLoadStatus.Current.Returns(_ => _loadStatus);
        _loadedLogNames.Current.Returns(_ => _loadedNames);
        _openLogsPresence.HasOpenLogs.Returns(_ => _loadedNames.Count > 0);
        _settings.TimeZoneInfo.Returns(TimeZoneInfo.Utc);
        _eventLogQueries.GetChannelNames().Returns(["System"]);
    }

    [Fact]
    public void ActiveFilterStatus_UsesRawUngroupedCount()
    {
        _filters = [.. Enumerable.Range(0, 1500).Select(index => SavedFilter.TryCreate($"Level == {index}")! with { IsEnabled = true })];

        var component = Render<UI.FilterPane.FilterPane>();

        Assert.Contains("[[FilterPane_ActiveFilters(1500)]]", component.Markup);
    }

    [Fact]
    public void Announcements_RouteReceivedMessagesThroughLocalizer()
    {
        _loadStatus = new LibraryLoadStatus(IsLoaded: true, LoadError: true);
        var component = Render<UI.FilterPane.FilterPane>();

        component.Instance.OpenFilterSetPicker();
        _announcements.Received(1).Announce("[[FilterPane_Announcement_LoadFailedRetryViaModal]]");

        _loadStatus = new LibraryLoadStatus(IsLoaded: false, LoadError: false);
        component.Instance.OpenFilterSetPicker();
        _announcements.Received(1).Announce("[[FilterPane_Announcement_LoadingTryAgain]]");

        _loadStatus = new LibraryLoadStatus(IsLoaded: true, LoadError: false);
        Assert.Equal("[[FilterPane_Announcement_RecentNoneAvailable]]", component.Instance.GetRecentDisabledReason());

        component.Instance.SelectedFilterSetId = new LibraryEntryId(Guid.NewGuid());
        component.Instance.TryResolveSelectedFilterSet();
        _announcements.Received(1).Announce("[[FilterPane_Announcement_SelectedFilterSetMissing]]");

        component.Instance.OpenScenarioPicker();
        component.Instance.ApplyScenarioSelection();
        _announcements.Received(1).Announce("[[FilterPane_Announcement_SelectedScenarioMissing]]");
    }

    [Theory]
    [InlineData(1, "[[FilterPane_ClearConfirm_Message_One]]")]
    [InlineData(2, "[[FilterPane_ClearConfirm_Message_Many(2)]]")]
    [InlineData(1500, "[[FilterPane_ClearConfirm_Message_Many(1500)]]")]
    public async Task ClearConfirm_RoutesOneAndManyMessagesWithRawCounts(int count, string expectedMessage)
    {
        var component = Render<UI.FilterPane.FilterPane>();
        AddPendingDrafts(component.Instance, count);

        await InvokePrivateTask(component.Instance, "ClearAllFiltersAsync");

        await _alertDialog.Received(1).ShowAlert(
            "[[FilterPane_ClearConfirm_Title]]",
            expectedMessage,
            "[[FilterPane_Action_Clear]]",
            "[[Modal_Cancel]]");
    }

    [Fact]
    public void DateEditor_RoutesLabelsAriaAndActionsThroughLocalizer()
    {
        var component = Render<UI.FilterPane.FilterPane>();
        component.Instance.EditDateFilter();
        component.Render();

        Assert.Contains("[[FilterPane_Date_AfterLabel]]", component.Markup);
        Assert.Contains("[[FilterPane_Date_BeforeLabel]]", component.Markup);
        Assert.Contains("[[FilterPane_Date_QuickRangeLabel]]", component.Markup);
        Assert.Equal("[[FilterPane_Date_AfterAria]]", component.Find("input.filter-datetime").GetAttribute("aria-label"));
        Assert.Contains("[[FilterPane_Date_QuickRangeAria]]", component.Markup);
        Assert.Contains("[[FilterPane_Action_Apply]]", component.Markup);
        Assert.Contains("[[FilterPane_Action_Remove]]", component.Markup);
    }

    [Fact]
    public void FilterSetEmptyState_RendersBothBranchesWithStrongEmphasisAndEncodedTemplateText()
    {
        Services.AddSingleton<IStringLocalizer<SharedResource>>(new EmptyStateSentinelLocalizer());
        var emptyComponent = Render<UI.FilterPane.FilterPane>();

        emptyComponent.Instance.OpenFilterSetPicker();
        emptyComponent.Render();

        AssertEmptyStateBranch(emptyComponent, "NoSavableFilterPane_FilterSetEmpty_NoSavableFilters");

        _filters = [SavedFilter.TryCreate("Level == 4")! with { IsEnabled = true }];
        var savableComponent = Render<UI.FilterPane.FilterPane>();
        savableComponent.Instance.OpenFilterSetPicker();
        savableComponent.Render();

        AssertEmptyStateBranch(savableComponent, "WithSavableFilterPane_FilterSetEmpty_WithSavableFilters");
    }

    [Fact]
    public void FilterSetPicker_RoutesLabelsOptionMetaAndRawCountsThroughLocalizer()
    {
        var filterSet = BuildFilterSet("Data <Set>", ["tagA", "tagB"], 1500);
        _library = [filterSet];
        var component = Render<UI.FilterPane.FilterPane>();

        component.Instance.OpenFilterSetPicker();
        component.Render();

        Assert.Contains("[[FilterPane_FilterSet_Label]]", component.Markup);
        Assert.Equal("[[FilterPane_FilterSet_SelectAria]]", component.Find(".filter-set-dropdown").GetAttribute("aria-label"));
        Assert.Contains("Data &lt;Set&gt;", component.Markup);
        Assert.Contains("[[FilterPane_FilterSetFilterCount_Many(1500)]]", component.Markup);
        Assert.Contains("[[FilterPane_FilterSetDetail_WithTags([[FilterPane_FilterSetFilterCount_Many(1500)]]|tagA, tagB)]]", component.Markup);
        Assert.Contains("[[FilterPane_FilterSetOptionMeta([[FilterPane_FilterSetDetail_WithTags([[FilterPane_FilterSetFilterCount_Many(1500)]]|tagA, tagB)]])]]", component.Markup);
        Assert.Equal("[[FilterPane_FilterSet_ReplaceAria]]", component.Find("button[aria-label='[[FilterPane_FilterSet_ReplaceAria]]']").GetAttribute("aria-label"));
    }

    [Theory]
    [InlineData(1, "[[FilterPane_FilterSetFilterCount_One(1)]]")]
    [InlineData(2, "[[FilterPane_FilterSetFilterCount_Many(2)]]")]
    [InlineData(1500, "[[FilterPane_FilterSetFilterCount_Many(1500)]]")]
    public void FilterSetPicker_RoutesOneManyAndLargeCountsThroughDistinctKeys(int count, string expectedCountMarker)
    {
        var filterSet = BuildFilterSet("Count Set", [], count);
        _library = [filterSet];
        var component = Render<UI.FilterPane.FilterPane>();

        component.Instance.OpenFilterSetPicker();
        component.Render();

        Assert.Contains(expectedCountMarker, component.Markup);
    }

    [Fact]
    public void HeaderChrome_RoutesActionsTitlesHintsAndRawActiveCountThroughLocalizer()
    {
        _filters = [SavedFilter.TryCreate("Level == 4")! with { IsEnabled = true }];
        _loadedNames = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "System");

        var component = Render<UI.FilterPane.FilterPane>();

        Assert.Equal("[[FilterPane_AddBasicTitle]]", component.Find(".split-button-primary").GetAttribute("title"));
        Assert.Contains("[[FilterPane_Action_AddFilter]]", component.Markup);
        Assert.Equal("[[FilterPane_AddMenuAria]]", component.Find(".split-button-chevron").GetAttribute("aria-label"));
        Assert.Contains("[[FilterPane_Action_AddExclusion]]", component.Markup);
        Assert.Contains("[[FilterPane_Action_AddDateFilter]]", component.Markup);
        Assert.Contains("[[FilterPane_Action_ApplyFilterSet]]", component.Markup);
        Assert.Contains("[[FilterPane_Action_ApplyScenario]]", component.Markup);
        Assert.Contains("[[FilterPane_ActiveFilters(1)]]", component.Markup);
        Assert.Equal("[[FilterPane_SaveAsFilterSet_Aria]]", component.Find("button[title='[[FilterPane_SaveAsFilterSet_TitleEnabled]]']").GetAttribute("aria-label"));
        Assert.Equal("[[FilterPane_ClearAll_Aria]]", component.Find("button[title='[[FilterPane_ClearAll_TitleEnabled]]']").GetAttribute("aria-label"));
        Assert.Equal("[[FilterPane_OpenLibrary_Aria]]", component.Find("button[title='[[FilterPane_OpenLibrary_Title]]']").GetAttribute("aria-label"));
        Assert.Equal("[[FilterPane_CopyScenarioJson_Aria]]", component.Find("button[title='[[FilterPane_CopyScenarioJson_Title]]']").GetAttribute("aria-label"));
        Assert.Equal("[[FilterPane_SaveScenarioJson_Aria]]", component.Find("button[title='[[FilterPane_SaveScenarioJson_Title]]']").GetAttribute("aria-label"));
        Assert.Contains("[[FilterPane_ScenarioExport_HintDisabled]]", component.Markup);
        Assert.Contains("[[FilterPane_SaveFilterSet_HintDisabled]]", component.Markup);
        Assert.Contains("[[FilterPane_ClearFilters_HintDisabled]]", component.Markup);
    }

    [Fact]
    public void HeaderChrome_WhenStateChanges_RoutesBothEnabledAndDisabledTitleBranches()
    {
        var component = Render<UI.FilterPane.FilterPane>();

        Assert.True(component.Find("button[aria-label='[[FilterPane_SaveAsFilterSet_Aria]]']").HasAttribute("disabled"));
        Assert.Equal("[[FilterPane_SaveAsFilterSet_TitleDisabled]]", component.Find("button[aria-label='[[FilterPane_SaveAsFilterSet_Aria]]']").GetAttribute("title"));
        Assert.True(component.Find("button[aria-label='[[FilterPane_ClearAll_Aria]]']").HasAttribute("disabled"));
        Assert.Equal("[[FilterPane_ClearAll_TitleDisabled]]", component.Find("button[aria-label='[[FilterPane_ClearAll_Aria]]']").GetAttribute("title"));

        _dateFilter = new DateFilter { IsEnabled = false };
        _filteredDateRange.Changed += Raise.Event<Action>();
        component.Render();

        Assert.True(component.Find("button[aria-label='[[FilterPane_SaveAsFilterSet_Aria]]']").HasAttribute("disabled"));
        Assert.Equal("save-filters-empty-hint", component.Find("button[aria-label='[[FilterPane_SaveAsFilterSet_Aria]]']").GetAttribute("aria-describedby"));
        Assert.False(component.Find("button[aria-label='[[FilterPane_ClearAll_Aria]]']").HasAttribute("disabled"));
        Assert.Equal("[[FilterPane_ClearAll_TitleEnabled]]", component.Find("button[aria-label='[[FilterPane_ClearAll_Aria]]']").GetAttribute("title"));

        _filters = [SavedFilter.TryCreate("Level == 4")! with { IsEnabled = true }];
        _activeFilters.Changed += Raise.Event<Action>();
        component.Render();

        Assert.False(component.Find("button[aria-label='[[FilterPane_SaveAsFilterSet_Aria]]']").HasAttribute("disabled"));
        Assert.Equal("[[FilterPane_SaveAsFilterSet_TitleEnabled]]", component.Find("button[aria-label='[[FilterPane_SaveAsFilterSet_Aria]]']").GetAttribute("title"));
    }

    [Fact]
    public async Task SaveScenario_WhenFileWriteFails_RoutesSaveDialogAndFailureAlertThroughLocalizer()
    {
        _filters = [SavedFilter.TryCreate("Level == 4")! with { IsEnabled = true }];
        _scenarioAuthoring.ExportRows(Arg.Any<IReadOnlyList<SavedFilter>>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(new ScenarioExportResult("{}", ImmutableList<string>.Empty, EmittedRowCount: 1));
        string missingDirectoryPath = Path.Combine("missing-scenario-export", Guid.NewGuid().ToString("N"), "scenario.json");
        _filePicker.PickSaveAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>())
            .Returns(missingDirectoryPath);
        var component = Render<UI.FilterPane.FilterPane>();

        await InvokePrivateTask(component.Instance, "SaveScenarioJsonAsync");

        await _filePicker.Received(1).PickSaveAsync("[[FilterPane_ExportScenario_Title]]", Arg.Is<IReadOnlyList<string>>(extensions => extensions != null && extensions.SequenceEqual(new[] { ".json" })), "scenario.json");
        await _alertDialog.Received(1).ShowAlert(
            "[[FilterPane_ExportFailed_Title]]",
            Arg.Any<string>(),
            "[[Modal_Accept]]");
    }

    [Fact]
    public async Task SaveScenario_WhenFileWritten_RoutesSavedPathThroughLocalizer()
    {
        _filters = [SavedFilter.TryCreate("Level == 4")! with { IsEnabled = true }];
        _scenarioAuthoring.ExportRows(Arg.Any<IReadOnlyList<SavedFilter>>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(new ScenarioExportResult("{}", ImmutableList<string>.Empty, EmittedRowCount: 1));
        string path = Path.Combine(AppContext.BaseDirectory, "scenario-localizer-success.json");
        _filePicker.PickSaveAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>())
            .Returns(path);
        var component = Render<UI.FilterPane.FilterPane>();

        try
        {
            await InvokePrivateTask(component.Instance, "SaveScenarioJsonAsync");

            _announcements.Received(1).Announce($"[[FilterPane_ScenarioSaved({path})]]");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ScenarioExportCallSites_RouteSingleFilterAndCurrentFiltersSubjects()
    {
        _filters = [SavedFilter.TryCreate("Level == 4")! with { IsEnabled = true }];
        _scenarioAuthoring.ExportRows(Arg.Any<IReadOnlyList<SavedFilter>>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(
                new ScenarioExportResult(string.Empty, ImmutableList<string>.Empty, EmittedRowCount: 0),
                new ScenarioExportResult(string.Empty, ["skipped detail"], EmittedRowCount: 0),
                new ScenarioExportResult(string.Empty, ImmutableList<string>.Empty, EmittedRowCount: 0));
        var component = Render<UI.FilterPane.FilterPane>();

        await InvokePrivateTask(component.Instance, "CopyActiveRowAsync", _filters[0]);
        await InvokePrivateTask(component.Instance, "CopyScenarioJsonAsync");
        await InvokePrivateTask(component.Instance, "SaveScenarioJsonAsync");

        _announcements.Received(1).Announce("[[ScenarioExport_NotExportable_SingleFilter_BasicOnly]]");
        _announcements.Received(1).Announce("[[ScenarioExport_NotExportable_CurrentFilters_WithDetail(skipped detail)]]");
        _announcements.Received(1).Announce("[[ScenarioExport_NotExportable_CurrentFilters_BasicOnly]]");
    }

    [Fact]
    public void ScenarioPicker_RoutesLabelsGroupDisplayOptionsAndNullFormatterThroughLocalizer()
    {
        _loadedNames = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "System");
        _scenarioQuery.GetInAppScenarios(Arg.Any<IReadOnlyCollection<string>>())
            .Returns([Scenario("sys", ScenarioGroup.SystemHealth), Scenario("sec", ScenarioGroup.Security)]);
        var component = Render<UI.FilterPane.FilterPane>();

        component.Instance.OpenScenarioPicker();
        component.Render();

        Assert.Equal(string.Empty, component.Instance.FormatScenarioGroup(null));
        Assert.Equal("[[Dashboard_Group_Security]]", component.Instance.FormatScenarioGroup(ScenarioGroup.Security));
        Assert.Contains("[[FilterPane_Scenario_CategoryLabel]]", component.Markup);
        var categoryDropdown = component.Find(".scenario-category-dropdown");
        Assert.Equal("[[FilterPane_Scenario_CategoryAria]]", categoryDropdown.GetAttribute("aria-label"));
        Assert.Contains(
            "[[Dashboard_Group_SystemHealth]]",
            categoryDropdown.ParentElement!.QuerySelector(".dropdown-list")!.TextContent,
            StringComparison.Ordinal);
        Assert.Contains("[[FilterPane_Scenario_Label]]", component.Markup);
        Assert.Equal("[[FilterPane_Scenario_SelectAria]]", component.Find(".scenario-item-dropdown").GetAttribute("aria-label"));
        Assert.Contains("[[FilterPane_Scenario_OptionTitle([[Dashboard_Group_SystemHealth]]|Purpose sys)]]", component.Markup);
        Assert.Contains("sys", component.Markup);
        Assert.DoesNotContain("[[sys]]", component.Markup);
    }

    private static void AddPendingDrafts(UI.FilterPane.FilterPane pane, int count)
    {
        var drafts = (List<FilterDraft>)typeof(UI.FilterPane.FilterPane)
            .GetField("_pendingDrafts", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(pane)!;

        for (int index = 0; index < count; index++)
        {
            drafts.Add(new FilterDraft { Mode = FilterMode.Basic });
        }
    }

    private static void AssertEmptyStateBranch(
        IRenderedComponent<UI.FilterPane.FilterPane> component,
        string expectedBranchMarker)
    {
        var empty = component.Find(".filter-empty-state");

        Assert.Contains(expectedBranchMarker, empty.TextContent, StringComparison.Ordinal);
        Assert.Single(empty.QuerySelectorAll("strong"));
        Assert.Equal("[[FilterPane_SaveAsFilterSet_EmphasisLabel]]", empty.QuerySelector("strong")!.TextContent);
        Assert.Contains($"{expectedBranchMarker}&lt;before&gt;&amp;", empty.InnerHtml, StringComparison.Ordinal);
        Assert.Empty(empty.QuerySelectorAll("before"));
        Assert.Empty(empty.QuerySelectorAll("after"));
    }

    private static LibraryEntryFilterSet BuildFilterSet(string name, ImmutableList<string> tags, int count) =>
        new()
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [.. Enumerable.Range(0, count).Select(index => SavedFilter.TryCreate($"Level == {index}")!)],
            Tags = tags,
        };

    private static async Task InvokePrivateTask(UI.FilterPane.FilterPane pane, string methodName, params object[] arguments)
    {
        var task = (Task?)typeof(UI.FilterPane.FilterPane)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(pane, arguments);

        Assert.NotNull(task);
        await task;
    }

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

    private sealed class EmptyStateSentinelLocalizer : IStringLocalizer<SharedResource>
    {
        private readonly MarkerLocalizer _inner = new();

        public LocalizedString this[string name] => name switch
        {
            "FilterPane_FilterSetEmpty_NoSavableFilters" => new(
                name,
                "NoSavableFilterPane_FilterSetEmpty_NoSavableFilters<before>&{0}<after>",
                resourceNotFound: false),
            "FilterPane_FilterSetEmpty_WithSavableFilters" => new(
                name,
                "WithSavableFilterPane_FilterSetEmpty_WithSavableFilters<before>&{0}<after>",
                resourceNotFound: false),
            _ => _inner[name],
        };

        public LocalizedString this[string name, params object[] arguments] => _inner[name, arguments];

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
