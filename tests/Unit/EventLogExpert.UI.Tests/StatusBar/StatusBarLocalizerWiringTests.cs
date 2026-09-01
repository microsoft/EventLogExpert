// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Localization;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Memory;
using EventLogExpert.Runtime.Stats;
using EventLogExpert.Runtime.StatusBar;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.Modal;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.StatusBar;

public sealed class StatusBarLocalizerWiringTests : BunitContext
{
    private readonly IEventLogCommands _eventLogCommands = Substitute.For<IEventLogCommands>();
    private readonly IFilterAppliedSource _filterApplied = Substitute.For<IFilterAppliedSource>();
    private readonly IFilterLensSource _lensSource = Substitute.For<IFilterLensSource>();
    private readonly IModalCoordinator _modalCoordinator = Substitute.For<IModalCoordinator>();
    private readonly IStatsCommands _statsCommands = Substitute.For<IStatsCommands>();
    private readonly IStatsVisibilitySource _statsVisibility = Substitute.For<IStatsVisibilitySource>();
    private readonly IStatusBarSource _statusBarSource = Substitute.For<IStatusBarSource>();
    private readonly IOrderedViewSource _viewSource = Substitute.For<IOrderedViewSource>();

    private EventLogId _activeLogId;
    private StatusBarPresentation _status = new();

    public StatusBarLocalizerWiringTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton<IStringLocalizer<SharedResource>>(new MarkerLocalizer());
        Services.AddSingleton(_eventLogCommands);
        Services.AddSingleton(_filterApplied);
        Services.AddSingleton(_lensSource);
        Services.AddSingleton(_modalCoordinator);
        Services.AddSingleton(_statsCommands);
        Services.AddSingleton(_statsVisibility);
        Services.AddSingleton(_statusBarSource);
        Services.AddSingleton(_viewSource);
        Services.AddSingleton(provider => new DisplayIndicatorGate(provider.GetRequiredService<IOrderedViewSource>(), (_, _) => Task.CompletedTask));

        _statusBarSource.Current.Returns(_ => _status);
        _filterApplied.IsFilteringEnabled.Returns(false);
        _lensSource.Lenses.Returns(ImmutableList<FilterLensSummary>.Empty);
        OrderedViewPresentation emptyPresentation = PresentationWithCount(0, EventLogId.Create(), PresentationState.Current);
        _viewSource.Current.Returns(emptyPresentation);
    }

    private static Filter Filtered => new(new DateFilter { IsEnabled = true }, []);

    [Fact]
    public void ActiveChrome_RoutesEachLocalizedSurfaceThroughTheLocalizer()
    {
        _lensSource.Lenses.Returns(LensSummaries(2));
        SetActiveChannel(total: 100, shown: 50, unresolved: 2, selected: 3);
        _filterApplied.IsFilteringEnabled.Returns(true);
        _status = _status with
        {
            IsPersistentFilterActive = true,
            LoadingActivities = ImmutableDictionary<StatusActivityId, LoadingProgress>.Empty
                .Add(StatusActivityId.Create(), new LoadingProgress(12, 3)),
            MemoryLevel = MemoryUsageLevel.Elevated,
            MemoryUsedMebibytes = 100,
            MemoryWorkingSetBytes = 256L * 1024 * 1024,
            NewEventBufferCount = 1000,
            NewEventBufferIsFull = true
        };

        var cut = Render<UI.StatusBar.StatusBar>();

        Assert.Equal("Application", cut.Find(".status-bar-source").TextContent);
        Assert.DoesNotContain("[[Application]]", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("[[StatusBar_Stats_Show]]", cut.Find("button.status-bar-stats").GetAttribute("title"));
        Assert.Contains("[[StatusBar_Counts_ShownOfTotalSelected(50|100|3)]]", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("[[StatusBar_Coverage_AriaLabel(2)]]", cut.Find(".status-bar-coverage").GetAttribute("aria-label"));
        Assert.Equal("[[StatusBar_Coverage_Tooltip(2|100)]]", cut.Find(".status-bar-coverage").GetAttribute("title"));
        Assert.Contains("[[StatusBar_Coverage_Chip(2)]]", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("[[StatusBar_Filter_ActiveLens_Many(2)]]", cut.Find(".status-bar-filter").GetAttribute("title"));
        Assert.Equal("[[StatusBar_Filter_Chip]]", cut.Find(".status-bar-filter").TextContent);
        Assert.Equal("[[StatusBar_Activity_BufferFull]]", cut.Find(".status-bar-announce").TextContent);
        Assert.Contains("[[StatusBar_Memory_Value_Elevated(100 MB)]]", cut.Find(".status-bar-memory").TextContent, StringComparison.Ordinal);
        Assert.Equal(
            "[[StatusBar_Memory_Tooltip_Elevated(100 MB|256 MB)]]",
            cut.Find(".status-bar-memory").GetAttribute("title"));
        Assert.Equal("[[StatusBar_Memory_Announce_Elevated]]", cut.Find(".status-bar-memory-announce").TextContent);
        Assert.Contains("[[StatusBar_Loading_Count(12)]]", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("[[StatusBar_Loading_Failed(3)]]", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("[[StatusBar_Activity_BufferFull]]", cut.Markup, StringComparison.Ordinal);

        var newEventsButton = cut.Find("button.status-bar-newevents");
        Assert.Equal("[[StatusBar_NewEvents_Load]]", newEventsButton.GetAttribute("title"));
        Assert.Equal("[[StatusBar_NewEvents_Label(1000)]]", newEventsButton.TextContent.Trim());
    }

    [Theory]
    [InlineData(1, "[[StatusBar_Counts_Total_One(1)]]")]
    [InlineData(2, "[[StatusBar_Counts_Total_Many(2)]]")]
    public void Counts_RoutesOneAndManyThroughDistinctLocalizerKeys(int total, string expected)
    {
        var localizer = new MarkerLocalizer();

        string actual = StatusBarTextComposer.Counts(localizer, total, total, isFiltered: false, selectedCount: 0);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void EmptySourceAndStatsHide_RouteThroughTheLocalizer()
    {
        _statsVisibility.IsVisible.Returns(true);
        SetActiveChannel(total: 100, shown: 100, unresolved: 0, selected: 0);

        var cut = Render<UI.StatusBar.StatusBar>();

        Assert.Equal("Application", cut.Find(".status-bar-source").TextContent);
        Assert.Equal("[[StatusBar_Stats_Hide]]", cut.Find("button.status-bar-stats").GetAttribute("title"));
    }

    [Fact]
    public void EmptySource_RoutesThroughTheLocalizer()
    {
        var cut = Render<UI.StatusBar.StatusBar>();

        Assert.Equal("[[StatusBar_Source_None]]", cut.Find(".status-bar-source").TextContent);
    }

    [Fact]
    public void FaultIndicator_RoutesAnnouncementThroughTheLocalizer()
    {
        SetActiveChannel(total: 100, shown: 0, unresolved: 0, selected: 0);
        OrderedViewPresentation faultedPresentation = PresentationWithCount(0, _activeLogId, PresentationState.Faulted);
        _viewSource.Current.Returns(faultedPresentation);

        var cut = Render<UI.StatusBar.StatusBar>();

        Assert.Equal("[[StatusBar_Activity_Fault]]", cut.Find(".status-bar-announce").TextContent);
    }

    [Theory]
    [InlineData(false, 1, "[[StatusBar_Filter_Lens_One]]")]
    [InlineData(true, 0, "[[StatusBar_Filter_Active]]")]
    [InlineData(true, 1, "[[StatusBar_Filter_ActiveLens_One]]")]
    public void FilterTooltip_RoutesMutuallyExclusiveBranchesThroughTheLocalizer(
        bool persistentFilterActive,
        int lensCount,
        string expected)
    {
        _lensSource.Lenses.Returns(LensSummaries(lensCount));
        SetActiveChannel(total: 100, shown: 50, unresolved: 2, selected: 0);
        _filterApplied.IsFilteringEnabled.Returns(true);
        _status = _status with { IsPersistentFilterActive = persistentFilterActive };

        var cut = Render<UI.StatusBar.StatusBar>();

        Assert.Equal(expected, cut.Find(".status-bar-filter").GetAttribute("title"));
    }

    [Fact]
    public void ResolverStatus_None_HidesResolverChip()
    {
        SetActiveChannel(total: 100, shown: 100, unresolved: 0, selected: 0);
        _status = _status with { ResolverStatus = ResolverStatus.None };

        var cut = Render<UI.StatusBar.StatusBar>();

        Assert.Empty(cut.FindAll(".status-bar-resolver"));
    }

    [Fact]
    public void ResolverStatus_RoutesThroughTheLocalizerAndPreemptsLocalizedIndicatorAnnouncement()
    {
        SetActiveChannel(total: 100, shown: 100, unresolved: 0, selected: 0);
        _status = _status with
        {
            ResolverStatus = ResolverStatus.FailedToLoad("Security.evtx"),
            NewEventBufferIsFull = true,
            LoadingActivities = ImmutableDictionary<StatusActivityId, LoadingProgress>.Empty
                .Add(StatusActivityId.Create(), new LoadingProgress(12, 0))
        };

        var cut = Render<UI.StatusBar.StatusBar>();

        Assert.Equal("[[StatusBar_Resolver_FailedToLoad(Security.evtx)]]", cut.Find(".status-bar-announce").TextContent);
        var resolver = cut.Find(".status-bar-resolver");
        Assert.Equal("[[StatusBar_Resolver_FailedToLoad(Security.evtx)]]", resolver.GetAttribute("title"));
        Assert.Equal("[[StatusBar_Resolver_FailedToLoad(Security.evtx)]]", resolver.TextContent);
        Assert.DoesNotContain("[[Security.evtx]]", cut.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, "[[StatusBar_Source_CombinedCount_One(1)]]")]
    [InlineData(2, "[[StatusBar_Source_CombinedCount_Many(2)]]")]
    public void Source_RoutesUnnamedGroupOneAndManyThroughDistinctLocalizerKeys(int memberCount, string expected)
    {
        var localizer = new MarkerLocalizer();
        var groupId = LogTabGroupId.Create();
        var active = new LogView(EventLogId.Create()) { GroupId = groupId };
        var members = Enumerable.Range(0, memberCount)
            .Select(_ => EventLogId.Create())
            .ToImmutableHashSet();
        var groups = new[] { new LogTabGroup(groupId, string.Empty, members) };

        string actual = StatusBarTextComposer.Source(localizer, active, [active], groups);

        Assert.Equal(expected, actual);
    }

    private static ImmutableList<FilterLensSummary> LensSummaries(int count) =>
        [.. Enumerable.Range(0, count).Select(_ => new FilterLensSummary(FilterLensId.Create(), new FilterLensLabel.ParentActivity(Guid.NewGuid())))];

    private static OrderedViewPresentation PresentationWithCount(int count, EventLogId tabId, PresentationState state)
    {
        var view = Substitute.For<IEventColumnView>();
        view.Count.Returns(count);

        return new OrderedViewPresentation(view, tabId, default, state, Revision: 1);
    }

    private void SetActiveChannel(int total, int shown, int unresolved, int selected)
    {
        var id = EventLogId.Create();
        _activeLogId = id;
        var log = new LogView(id) { LogName = "Application", LogPathType = LogPathType.Channel };

        OrderedViewPresentation presentation = PresentationWithCount(shown, id, PresentationState.Current);
        _viewSource.Current.Returns(presentation);
        _filterApplied.IsFilteringEnabled.Returns(Filtered.IsFilteringEnabled);
        _status = new StatusBarPresentation
        {
            Tabs = ImmutableList.Create(log),
            ActiveTabId = id,
            RawEventTotal = total,
            RawEventCountsByLog = ImmutableDictionary<EventLogId, ProviderResolutionCounts>.Empty.Add(
                id,
                new ProviderResolutionCounts(total, total - unresolved, unresolved, 0, 0)),
            SelectionCount = selected
        };
    }
}
