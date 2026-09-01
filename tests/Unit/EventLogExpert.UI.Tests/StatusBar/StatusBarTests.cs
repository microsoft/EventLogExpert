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
using EventLogExpert.UI.LogTable.Resolution;
using EventLogExpert.UI.Modal;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;
using System.Collections.Immutable;
using System.Globalization;

namespace EventLogExpert.UI.Tests.StatusBar;

public sealed class StatusBarTests : CultureSensitiveBunitContext
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

    public StatusBarTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

        Services.AddSingleton(_eventLogCommands);
        Services.AddSingleton(_filterApplied);
        Services.AddSingleton(_lensSource);
        Services.AddSingleton(_modalCoordinator);
        Services.AddSingleton(_statsCommands);
        Services.AddSingleton(_statsVisibility);
        Services.AddSingleton(_statusBarSource);
        Services.AddSingleton(_viewSource);

        Services.AddSingleton<IStringLocalizer<SharedResource>>(new MarkerLocalizer());
        Services.AddSingleton(provider => new DisplayIndicatorGate(provider.GetRequiredService<IOrderedViewSource>()));

        _statusBarSource.Current.Returns(_ => _status);
        _filterApplied.IsFilteringEnabled.Returns(false);
        _lensSource.Lenses.Returns(ImmutableList<FilterLensSummary>.Empty);

        var emptyPresentation = PresentationWithCount(0, EventLogId.Create());

        _viewSource.Current.Returns(emptyPresentation);
    }

    private static Filter Filtered => new(new DateFilter { IsEnabled = true }, []);

    private static Filter Unfiltered => new(null, []);

    [Fact]
    public void ACountFromASettledView_IsStillReportedAsAFilteredResult()
    {
        SetActiveLog(total: 1500, shown: 0, filter: Filtered, selected: 0);

        var cut = Render<UI.StatusBar.StatusBar>();

        Assert.Contains("[[StatusBar_Counts_ShownOfTotal(0|1,500)]]", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ACountFromAViewStillBeingBuilt_IsNotPassedOffAsAFilteredResult()
    {
        SetActiveLog(total: 1500, shown: 0, filter: Filtered, selected: 0);

        _viewSource.Current.Returns(new OrderedViewPresentation(
            LogTableState.EmptyView, _activeLogId, default, PresentationState.Updating, Revision: 2));

        var cut = Render<UI.StatusBar.StatusBar>();

        Assert.DoesNotContain("[[StatusBar_Counts_ShownOfTotal(0|1,500)]]", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("[[StatusBar_Counts_Total_Many(1,500)]]", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ADisplayThatKeepsTheUserWaiting_IsAnnouncedThroughTheOneLiveRegion()
    {
        var delay = new ManualDelay();

        Services.AddSingleton(_ => new DisplayIndicatorGate(_viewSource, delay.Delay));

        var id = EventLogId.Create();
        _activeLogId = id;

        var pending = new OrderedViewPresentation(
            LogTableState.EmptyView, id, default, PresentationState.Updating, Revision: 1);

        _status = new StatusBarPresentation
        {
            Tabs = ImmutableList.Create(new LogView(id) { LogName = "Application", LogPathType = LogPathType.Channel }),
            ActiveTabId = id,
            RawEventCountsByLog = ImmutableDictionary<EventLogId, ProviderResolutionCounts>.Empty.Add(id, default)
        };
        _viewSource.Current.Returns(pending);

        var cut = Render<UI.StatusBar.StatusBar>();

        Assert.DoesNotContain("[[StatusBar_Activity_LoadingEvents]]", cut.Find(".status-bar-announce").TextContent, StringComparison.Ordinal);

        delay.Elapse();

        cut.WaitForAssertion(() =>
            Assert.Contains("[[StatusBar_Activity_LoadingEvents]]", cut.Find(".status-bar-announce").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void ALensChangeAfterRender_RepaintsTheLensTooltip_ThroughTheSource()
    {
        _lensSource.Lenses.Returns(LensSummaries(2));
        SetActiveLog(total: 1500, shown: 300, filter: Filtered, selected: 0);

        var cut = Render<UI.StatusBar.StatusBar>();

        Assert.Equal("[[StatusBar_Filter_Lens_Many(2)]]", cut.Find(".status-bar-filter").GetAttribute("title"));

        _lensSource.Lenses.Returns(LensSummaries(3));
        cut.InvokeAsync(() => _lensSource.Changed += Raise.Event<Action>());

        cut.WaitForAssertion(() => Assert.Equal("[[StatusBar_Filter_Lens_Many(3)]]", cut.Find(".status-bar-filter").GetAttribute("title")));
    }

    [Fact]
    public void ChannelNewEventsCounter_IsNotAnnounced()
    {
        var id = EventLogId.Create();
        var channel = new LogView(id) { LogName = "Application", LogPathType = LogPathType.Channel };
        _status = new StatusBarPresentation
        {
            Tabs = ImmutableList.Create(channel),
            ActiveTabId = id,
            RawEventCountsByLog = ImmutableDictionary<EventLogId, ProviderResolutionCounts>.Empty.Add(id, default),
            NewEventBufferCount = 42
        };

        var cut = Render<UI.StatusBar.StatusBar>();

        var newEvents = cut.FindAll(".status-bar-activity").Single(node => node.TextContent.Contains("[[StatusBar_NewEvents_Label(42)]]", StringComparison.Ordinal));
        Assert.Equal("off", newEvents.GetAttribute("aria-live"));
    }

    [Fact]
    public void ContinuouslyUpdating_RendersNoNewEventsButton()
    {
        SetChannelStatus(newEventCount: 5, continuous: true);

        var cut = Render<UI.StatusBar.StatusBar>();

        Assert.Empty(cut.FindAll("button.status-bar-newevents"));
        Assert.Contains("[[StatusBar_Activity_ContinuouslyUpdating]]", cut.Find(".status-bar-live").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void CoverageChip_Click_OpensCoverageModal()
    {
        SetActiveLog(total: 100, shown: 100, filter: Unfiltered, selected: 0);

        var cut = Render<UI.StatusBar.StatusBar>();
        cut.Find(".status-bar-coverage").Click();

        _modalCoordinator.Received(1).PushAsync<ResolutionCoverageModal, bool>(Arg.Any<IDictionary<string, object?>?>());
    }

    [Fact]
    public async Task Disposal_UnsubscribesFromTheSource()
    {
        SetActiveLog(total: 500, shown: 500, filter: Unfiltered, selected: 0);

        var cut = Render<UI.StatusBar.StatusBar>();

        await cut.Instance.DisposeAsync();

        _viewSource.Received(1).Updated -= Arg.Any<Action<OrderedViewPresentation>>();
        _statusBarSource.Received(1).Changed -= Arg.Any<Action>();
        _filterApplied.Received(1).Changed -= Arg.Any<Action>();
        _lensSource.Received(1).Changed -= Arg.Any<Action>();
    }

    [Fact]
    public void FileLogOnly_RendersNoNewEventsButton()
    {
        var id = EventLogId.Create();
        _activeLogId = id;
        var file = new LogView(id) { LogName = "app.evtx", LogPathType = LogPathType.File };
        _status = new StatusBarPresentation
        {
            Tabs = ImmutableList.Create(file),
            ActiveTabId = id,
            RawEventCountsByLog = ImmutableDictionary<EventLogId, ProviderResolutionCounts>.Empty.Add(id, default)
        };

        Assert.Empty(Render<UI.StatusBar.StatusBar>().FindAll("button.status-bar-newevents"));
    }

    [Fact]
    public void Filtered_ShowsShownOfTotal_AndFilteredIndicator()
    {
        SetActiveLog(total: 1500, shown: 200, filter: Filtered, selected: 0);
        _status = _status with { IsPersistentFilterActive = true };

        var cut = Render<UI.StatusBar.StatusBar>();

        Assert.Contains("[[StatusBar_Counts_ShownOfTotal(200|1,500)]]", cut.Markup, StringComparison.Ordinal);

        var indicator = cut.Find(".status-bar-filter");
        Assert.Equal("[[StatusBar_Filter_Active]]", indicator.GetAttribute("title"));
    }

    [Fact]
    public void LensOnlyNarrowing_ShowsShown_AndLensTooltip()
    {
        _lensSource.Lenses.Returns(LensSummaries(2));
        SetActiveLog(total: 1500, shown: 300, filter: Filtered, selected: 0);

        var cut = Render<UI.StatusBar.StatusBar>();

        Assert.Contains("[[StatusBar_Counts_ShownOfTotal(300|1,500)]]", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("[[StatusBar_Filter_Lens_Many(2)]]", cut.Find(".status-bar-filter").GetAttribute("title"));
    }

    [Fact]
    public void LoadingActivity_RendersLoadedAndFailedCounts()
    {
        _status = new StatusBarPresentation
        {
            LoadingActivities = ImmutableDictionary<StatusActivityId, LoadingProgress>.Empty
                .Add(new StatusActivityId(Guid.NewGuid()), new LoadingProgress(12, 3))
        };

        var cut = Render<UI.StatusBar.StatusBar>();

        Assert.Contains("[[StatusBar_Loading_Count(12)]]", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("[[StatusBar_Loading_Failed(3)]]", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void MemoryChip_Elevated_RendersTheElevatedBandAndLevelWord()
    {
        _status = new StatusBarPresentation { MemoryLevel = MemoryUsageLevel.Elevated, MemoryUsedMebibytes = 100 };

        var cut = Render<UI.StatusBar.StatusBar>();

        var chip = cut.Find(".status-bar-memory");
        Assert.Contains("status-bar-memory-elevated", chip.ClassList);
        Assert.Contains("[[StatusBar_Memory_Value_Elevated(100 MB)]]", chip.TextContent, StringComparison.Ordinal);
        Assert.Equal("[[StatusBar_Memory_Announce_Elevated]]", cut.Find(".status-bar-memory-announce").TextContent);
    }

    [Fact]
    public void MemoryChip_High_RendersTheHighBandAndLevelWord()
    {
        _status = new StatusBarPresentation { MemoryLevel = MemoryUsageLevel.High, MemoryUsedMebibytes = 100 };

        var cut = Render<UI.StatusBar.StatusBar>();

        var chip = cut.Find(".status-bar-memory");
        Assert.Contains("status-bar-memory-high", chip.ClassList);
        Assert.Contains("[[StatusBar_Memory_Value_High(100 MB)]]", chip.TextContent, StringComparison.Ordinal);
        Assert.Equal("[[StatusBar_Memory_Announce_High]]", cut.Find(".status-bar-memory-announce").TextContent);
    }

    [Fact]
    public void MemoryChip_Normal_RendersPlainTextWithoutABandClass()
    {
        _status = new StatusBarPresentation { MemoryLevel = MemoryUsageLevel.Normal, MemoryUsedMebibytes = 100 };

        var cut = Render<UI.StatusBar.StatusBar>();

        var chip = cut.Find(".status-bar-memory");
        Assert.Contains("[[StatusBar_Memory_Value_Normal(100 MB)]]", chip.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("status-bar-memory-elevated", chip.ClassList);
        Assert.DoesNotContain("status-bar-memory-high", chip.ClassList);
        Assert.Equal("[[StatusBar_Memory_Announce_Normal]]", cut.Find(".status-bar-memory-announce").TextContent);
    }

    [Fact]
    public void MemoryChip_Tooltip_ReconcilesManagedHeapAndWorkingSet()
    {
        _status = new StatusBarPresentation
        {
            MemoryLevel = MemoryUsageLevel.Normal,
            MemoryUsedMebibytes = 100,
            MemoryWorkingSetBytes = 256L * 1024 * 1024
        };

        var cut = Render<UI.StatusBar.StatusBar>();

        var tooltip = cut.Find(".status-bar-memory").GetAttribute("title");
        Assert.Equal("[[StatusBar_Memory_Tooltip_Normal(100 MB|256 MB)]]", tooltip);
    }

    [Fact]
    public void MemoryLevelChange_ThroughTheSource_RepaintsTheChipAndAnnouncement()
    {
        _status = new StatusBarPresentation { MemoryLevel = MemoryUsageLevel.Normal, MemoryUsedMebibytes = 100 };

        var cut = Render<UI.StatusBar.StatusBar>();

        Assert.DoesNotContain("status-bar-memory-high", cut.Find(".status-bar-memory").ClassList);

        _status = new StatusBarPresentation { MemoryLevel = MemoryUsageLevel.High, MemoryUsedMebibytes = 900 };
        cut.InvokeAsync(() => _statusBarSource.Changed += Raise.Event<Action>());

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("status-bar-memory-high", cut.Find(".status-bar-memory").ClassList);
            Assert.Equal("[[StatusBar_Memory_Announce_High]]", cut.Find(".status-bar-memory-announce").TextContent);
        });
    }

    [Fact]
    public void MultiSelect_ShowsSelectedSuffix_SingleSelectDoesNot()
    {
        SetActiveLog(total: 500, shown: 500, filter: Unfiltered, selected: 3);
        Assert.Contains("[[StatusBar_Counts_TotalSelected(500|3)]]", Render<UI.StatusBar.StatusBar>().Markup, StringComparison.Ordinal);

        _status = _status with { SelectionCount = 1 };
        Assert.DoesNotContain("[[StatusBar_Counts_TotalSelected", Render<UI.StatusBar.StatusBar>().Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void MultipleLoadingActivities_RenderAsASingleAggregateChip()
    {
        _status = new StatusBarPresentation
        {
            LoadingActivities = ImmutableDictionary<StatusActivityId, LoadingProgress>.Empty
                .Add(new StatusActivityId(Guid.NewGuid()), new LoadingProgress(100, 0))
                .Add(new StatusActivityId(Guid.NewGuid()), new LoadingProgress(50, 2))
        };

        var cut = Render<UI.StatusBar.StatusBar>();

        Assert.Contains("[[StatusBar_Loading_ManyLogs(2)]]", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("[[StatusBar_Loading_Count(100)]]", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("[[StatusBar_Loading_Count(50)]]", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("[[StatusBar_Loading_Failed(2)]]", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void NewEventsButton_BufferFull_CoexistsWithWarnChip_AndIsClickable()
    {
        SetChannelStatus(newEventCount: 1000, bufferFull: true);

        var cut = Render<UI.StatusBar.StatusBar>();
        var button = cut.Find("button.status-bar-newevents");
        Assert.False(button.HasAttribute("disabled"));
        Assert.Contains(cut.FindAll(".status-bar-warn"), node => node.TextContent.Contains("[[StatusBar_Activity_BufferFull]]", StringComparison.Ordinal));

        button.Click();

        _eventLogCommands.Received(1).LoadNewEvents();
    }

    [Fact]
    public void NewEventsButton_Click_LoadsNewEvents()
    {
        SetChannelStatus(newEventCount: 42);

        var button = Render<UI.StatusBar.StatusBar>().Find("button.status-bar-newevents");
        Assert.Contains("[[StatusBar_NewEvents_Label(42)]]", button.TextContent, StringComparison.Ordinal);
        Assert.False(button.HasAttribute("aria-label"));
        Assert.Equal("[[StatusBar_NewEvents_Load]]", button.GetAttribute("title"));

        button.Click();

        _eventLogCommands.Received(1).LoadNewEvents();
    }

    [Fact]
    public void NewEventsButton_IsTheLastRightClusterItem_EvenWhenBufferFull()
    {
        SetChannelStatus(newEventCount: 1000, bufferFull: true);

        var cut = Render<UI.StatusBar.StatusBar>();

        var right = cut.Find(".status-bar-right");
        var children = right.Children.ToList();
        var memoryChip = right.QuerySelector(".status-bar-memory");
        var newEventsButton = right.QuerySelector("button.status-bar-newevents");

        Assert.NotNull(memoryChip);
        Assert.NotNull(newEventsButton);
        Assert.Equal(children.Count - 1, children.IndexOf(newEventsButton));
        Assert.True(children.IndexOf(memoryChip) < children.IndexOf(newEventsButton));
    }

    [Fact]
    public void NewEventsButton_ZeroCount_IsRenderedAndEnabled()
    {
        SetChannelStatus(newEventCount: 0);

        var button = Render<UI.StatusBar.StatusBar>().Find("button.status-bar-newevents");

        Assert.False(button.HasAttribute("disabled"));
        Assert.Contains("[[StatusBar_NewEvents_Label(0)]]", button.TextContent, StringComparison.Ordinal);
        Assert.Equal("[[StatusBar_NewEvents_None]]", button.GetAttribute("title"));
    }

    [Fact]
    public void NewEventsCounter_StaysVisibleWhileAnotherLogLoads()
    {
        var id = EventLogId.Create();
        var channel = new LogView(id) { LogName = "Application", LogPathType = LogPathType.Channel };
        _status = new StatusBarPresentation
        {
            Tabs = ImmutableList.Create(channel),
            ActiveTabId = id,
            RawEventCountsByLog = ImmutableDictionary<EventLogId, ProviderResolutionCounts>.Empty.Add(id, default),
            NewEventBufferCount = 42,
            LoadingActivities = ImmutableDictionary<StatusActivityId, LoadingProgress>.Empty
                .Add(new StatusActivityId(Guid.NewGuid()), new LoadingProgress(500, 0))
        };

        var cut = Render<UI.StatusBar.StatusBar>();

        Assert.Contains("[[StatusBar_Loading_Count(500)]]", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("[[StatusBar_NewEvents_Label(42)]]", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void NoActiveLog_ShowsNoLogOpen_AndNoCounts()
    {
        var cut = Render<UI.StatusBar.StatusBar>();

        Assert.Contains("[[StatusBar_Source_None]]", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".status-bar-counts"));
    }

    [Fact]
    public void PresentationForADifferentTab_NeverPairsThisTabsTotalWithThatTabsCount()
    {
        SetActiveLog(total: 1500, shown: 200, filter: Filtered, selected: 0);

        var otherTab = PresentationWithCount(200, EventLogId.Create());

        _viewSource.Current.Returns(otherTab);

        var cut = Render<UI.StatusBar.StatusBar>();

        Assert.DoesNotContain("[[StatusBar_Counts_ShownOfTotal(200|1,500)]]", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("[[StatusBar_Counts_ShownOfTotal", cut.Markup, StringComparison.Ordinal);

        Assert.Contains("[[StatusBar_Counts_Total_Many(1,500)]]", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishedPresentation_RendersTheNewCount()
    {
        SetActiveLog(total: 1500, shown: 200, filter: Filtered, selected: 0);

        var cut = Render<UI.StatusBar.StatusBar>();

        Assert.Contains("[[StatusBar_Counts_ShownOfTotal(200|1,500)]]", cut.Markup, StringComparison.Ordinal);

        var grown = PresentationWithCount(275, _activeLogId);

        _viewSource.Current.Returns(grown);
        _viewSource.Updated += Raise.Event<Action<OrderedViewPresentation>>(grown);

        cut.WaitForAssertion(() => Assert.Contains("[[StatusBar_Counts_ShownOfTotal(275|1,500)]]", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void ResolverError_RendersInATruncatingSpanWithFullTitle()
    {
        _status = new StatusBarPresentation { ResolverStatus = ResolverStatus.FailedToLoad("Security.evtx") };

        var cut = Render<UI.StatusBar.StatusBar>();

        var resolver = cut.Find(".status-bar-resolver");
        Assert.Equal("[[StatusBar_Resolver_FailedToLoad(Security.evtx)]]", resolver.GetAttribute("title"));
        Assert.Equal("[[StatusBar_Resolver_FailedToLoad(Security.evtx)]]", resolver.TextContent);
        Assert.DoesNotContain("[[Security.evtx]]", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Root_HasNoLiveRegion_ButAnnounceRegionIsPoliteStatus()
    {
        SetActiveLog(total: 500, shown: 500, filter: Unfiltered, selected: 0);

        var cut = Render<UI.StatusBar.StatusBar>();

        var root = cut.Find(".status-bar");
        Assert.False(root.HasAttribute("aria-live"));
        Assert.False(root.HasAttribute("role"));

        var announce = cut.Find(".status-bar-announce");
        Assert.Equal("status", announce.GetAttribute("role"));
        Assert.Equal("polite", announce.GetAttribute("aria-live"));
    }

    [Fact]
    public void SingleLoadingActivityAtZero_RendersPendingTextNotZero()
    {
        _status = new StatusBarPresentation
        {
            LoadingActivities = ImmutableDictionary<StatusActivityId, LoadingProgress>.Empty
                .Add(new StatusActivityId(Guid.NewGuid()), new LoadingProgress(0, 0))
        };

        var cut = Render<UI.StatusBar.StatusBar>();

        Assert.Contains("[[StatusBar_Loading_Pending]]", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("[[StatusBar_Loading_Count(0)]]", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SingleLoadingActivityWithTotal_RendersPercent()
    {
        _status = new StatusBarPresentation
        {
            LoadingActivities = ImmutableDictionary<StatusActivityId, LoadingProgress>.Empty
                .Add(new StatusActivityId(Guid.NewGuid()), new LoadingProgress(4_500, 0, 10_000))
        };

        var cut = Render<UI.StatusBar.StatusBar>();

        Assert.Contains("[[StatusBar_Loading_CountPercent(4,500|45)]]", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SingleLoadingActivity_GroupsLargeFailedCount()
    {
        _status = new StatusBarPresentation
        {
            LoadingActivities = ImmutableDictionary<StatusActivityId, LoadingProgress>.Empty
                .Add(new StatusActivityId(Guid.NewGuid()), new LoadingProgress(5000, 1500))
        };

        var cut = Render<UI.StatusBar.StatusBar>();

        Assert.Contains("[[StatusBar_Loading_Failed(1,500)]]", cut.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "[[StatusBar_Stats_Show]]")]
    [InlineData(true, "[[StatusBar_Stats_Hide]]")]
    public void StatsChip_Title_RoutesShowOrHideKeyByVisibility(bool isVisible, string expected)
    {
        SetActiveLog(total: 100, shown: 100, filter: Unfiltered, selected: 0);
        _statsVisibility.IsVisible.Returns(isVisible);

        var cut = Render<UI.StatusBar.StatusBar>();

        Assert.Equal(expected, cut.Find("button.status-bar-stats:not(.status-bar-coverage)").GetAttribute("title"));
    }

    [Fact]
    public void StatsChip_TogglesStatisticsVisibility_ThroughTheCommand()
    {
        SetActiveLog(total: 100, shown: 100, filter: Unfiltered, selected: 0);
        _statsVisibility.IsVisible.Returns(false);

        var cut = Render<UI.StatusBar.StatusBar>();
        cut.Find("button.status-bar-stats").Click();

        _statsCommands.Received(1).SetVisible(true);
    }

    [Fact]
    public void Unfiltered_ShowsTotalEvents_WithoutFilteredIndicator()
    {
        SetActiveLog(total: 1500, shown: 1500, filter: Unfiltered, selected: 0);

        var cut = Render<UI.StatusBar.StatusBar>();

        Assert.Contains("[[StatusBar_Counts_Total_Many(1,500)]]", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("[[StatusBar_Counts_ShownOfTotal", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".status-bar-filter"));
    }

    private static ImmutableList<FilterLensSummary> LensSummaries(int count) =>
        [.. Enumerable.Range(0, count).Select(_ => new FilterLensSummary(FilterLensId.Create(), new FilterLensLabel.ParentActivity(Guid.NewGuid())))];

    private static OrderedViewPresentation PresentationWithCount(int count, EventLogId tabId)
    {
        var view = Substitute.For<IEventColumnView>();
        view.Count.Returns(count);

        return new OrderedViewPresentation(
            view,
            tabId,
            default,
            PresentationState.Current,
            Revision: 1);
    }

    private void SetActiveLog(int total, int shown, Filter filter, int selected)
    {
        var id = EventLogId.Create();
        _activeLogId = id;
        var log = new LogView(id) { LogName = "Application", LogPathType = LogPathType.Channel };
        var presentation = PresentationWithCount(shown, id);

        _viewSource.Current.Returns(presentation);
        _filterApplied.IsFilteringEnabled.Returns(filter.IsFilteringEnabled);
        _status = new StatusBarPresentation
        {
            Tabs = ImmutableList.Create(log),
            ActiveTabId = id,
            RawEventTotal = total,
            RawEventCountsByLog = ImmutableDictionary<EventLogId, ProviderResolutionCounts>.Empty.Add(id, new ProviderResolutionCounts(total, total, 0, 0, 0)),
            SelectionCount = selected
        };
    }

    private void SetChannelStatus(int newEventCount, bool bufferFull = false, bool continuous = false)
    {
        var id = EventLogId.Create();
        _activeLogId = id;
        var channel = new LogView(id) { LogName = "Application", LogPathType = LogPathType.Channel };
        _status = new StatusBarPresentation
        {
            Tabs = ImmutableList.Create(channel),
            ActiveTabId = id,
            RawEventCountsByLog = ImmutableDictionary<EventLogId, ProviderResolutionCounts>.Empty.Add(id, default),
            NewEventBufferCount = newEventCount,
            NewEventBufferIsFull = bufferFull,
            ContinuouslyUpdate = continuous
        };
    }

    private sealed class ManualDelay
    {
        private readonly List<TaskCompletionSource> _pending = [];
        private readonly Lock _sync = new();

        public Task Delay(TimeSpan duration, CancellationToken token)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_sync) { _pending.Add(completion); }

            return completion.Task;
        }

        public void Elapse()
        {
            TaskCompletionSource[] outstanding;

            lock (_sync)
            {
                outstanding = [.. _pending];

                _pending.Clear();
            }

            foreach (var completion in outstanding) { completion.TrySetResult(); }
        }
    }
}
