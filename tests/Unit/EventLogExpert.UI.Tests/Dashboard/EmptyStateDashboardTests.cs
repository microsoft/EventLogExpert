// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using AngleSharp.Dom;
using Bunit;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Writers;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Scenarios;
using EventLogExpert.Runtime.Scenarios.Favorites;
using EventLogExpert.Scenarios.Catalog;
using EventLogExpert.UI.Dashboard;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.Dashboard;

public sealed class EmptyStateDashboardTests : BunitContext
{
    private const string ActiveDetailLaunch = ".sidebar-tabs-tabpanel.active .scenario-detail__launch";
    private const string ActiveDetailName = ".sidebar-tabs-tabpanel.active .scenario-detail__name";
    private const string ActiveDetailOpenFolder = ".sidebar-tabs-tabpanel.active .scenario-detail__open-folder";
    private const string ActiveDetailStar = ".sidebar-tabs-tabpanel.active .scenario-detail__star";
    private const string ActiveOption = ".sidebar-tabs-tabpanel.active [role='option']";

    private readonly IMenuActionService _actions = Substitute.For<IMenuActionService>();
    private readonly IAlertDialogService _alertDialog = Substitute.For<IAlertDialogService>();
    private readonly IAnnouncementService _announcer = Substitute.For<IAnnouncementService>();
    private readonly IChannelEnableService _channelEnable = Substitute.For<IChannelEnableService>();
    private readonly IChannelReadinessService _channelReadinessService = Substitute.For<IChannelReadinessService>();
    private readonly IScenarioFavoriteCommands _favoriteCommands = Substitute.For<IScenarioFavoriteCommands>();
    private readonly IScenarioFavoritesSource _favoritesSource = Substitute.For<IScenarioFavoritesSource>();
    private readonly IFilterAppliedSource _filterAppliedSource = Substitute.For<IFilterAppliedSource>();
    private readonly IFilterPaneCommands _filterCommands = Substitute.For<IFilterPaneCommands>();
    private readonly IScenarioLaunchService _scenarioLaunch = Substitute.For<IScenarioLaunchService>();
    private readonly IScenarioQueryService _scenarioQuery = Substitute.For<IScenarioQueryService>();

    public EmptyStateDashboardTests()
    {
        _scenarioLaunch.LaunchAsync(Arg.Any<ScenarioDefinition>(), Arg.Any<DateFilter?>(), Arg.Any<bool>())
            .Returns(new ScenarioLaunchResult(1, 0, 0));
        _scenarioQuery.GetSplashScenarios().Returns([]);
        _channelReadinessService.GetReadinessAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                new ChannelReadiness("System", ChannelPresence.Present, ChannelEnablement.Enabled)
                {
                    Access = ChannelAccess.Accessible
                }
            ]);
        _favoritesSource.FavoriteScenarioIds.Returns(ImmutableHashSet<string>.Empty);

        Services.AddSingleton(_actions);
        Services.AddSingleton(_alertDialog);
        Services.AddSingleton(_announcer);
        Services.AddSingleton(_favoriteCommands);
        Services.AddSingleton(_favoritesSource);
        Services.AddSingleton(_filterAppliedSource);
        Services.AddSingleton(_filterCommands);
        Services.AddSingleton(_scenarioLaunch);
        Services.AddSingleton(_scenarioQuery);
        Services.AddSingleton(_channelReadinessService);
        Services.AddSingleton(_channelEnable);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Catalog_WhenNoCategories_ShowsEmptyState()
    {
        var cut = Render<EmptyStateDashboard>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".empty-dashboard__empty")));
        Assert.Empty(cut.FindAll(".sidebar-tabs"));
    }

    [Fact]
    public void Catalog_WhenScenariosLoading_ShowsLoading()
    {
        using var gate = new ManualResetEventSlim(false);
        _scenarioQuery.GetSplashScenarios().Returns(_ => { gate.Wait(); return []; });

        var cut = Render<EmptyStateDashboard>();

        Assert.NotEmpty(cut.FindAll(".empty-dashboard__loading"));

        gate.Set();
    }

    [Fact]
    public void Chip_TogglesReactively_WhenFilterAppliedSourceRaisesChanged()
    {
        _filterAppliedSource.IsFilteringEnabled.Returns(false);

        var cut = Render<EmptyStateDashboard>();
        Assert.Empty(cut.FindAll(".empty-dashboard__chip"));

        _filterAppliedSource.IsFilteringEnabled.Returns(true);
        cut.InvokeAsync(() => _filterAppliedSource.Changed += Raise.Event<Action>());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".empty-dashboard__chip")));

        _filterAppliedSource.IsFilteringEnabled.Returns(false);
        cut.InvokeAsync(() => _filterAppliedSource.Changed += Raise.Event<Action>());
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".empty-dashboard__chip")));
    }

    [Fact]
    public void Chip_WhenFilterApplied_ClearInvokesClearAllFilters()
    {
        _filterAppliedSource.IsFilteringEnabled.Returns(true);

        var cut = Render<EmptyStateDashboard>();
        cut.Find(".empty-dashboard__chip button").Click();

        cut.WaitForAssertion(() => _filterCommands.Received(1).ClearAllFilters());
    }

    [Fact]
    public void Chip_WhenNoFilterApplied_NotRendered()
    {
        _filterAppliedSource.IsFilteringEnabled.Returns(false);

        var cut = Render<EmptyStateDashboard>();

        Assert.Empty(cut.FindAll(".empty-dashboard__chip"));
    }

    [Fact]
    public void DetailLaunch_InvokesScenarioLaunchAndAnnounces()
    {
        _scenarioQuery.GetSplashScenarios().Returns([Scenario("application-crashes", "Application crashes")]);

        var cut = Render<EmptyStateDashboard>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(ActiveDetailLaunch)));

        cut.Find(ActiveDetailLaunch).Click();

        cut.WaitForAssertion(() =>
        {
            _scenarioLaunch.Received(1)
                .LaunchAsync(Arg.Is<ScenarioDefinition>(scenario => scenario != null && scenario.Id == "application-crashes"), null);
            _announcer.Received(1).Announce(Arg.Any<string>());
        });
    }

    [Fact]
    public void DetailLaunch_WhenAccessDenied_ShowsFolderFallbackAction()
    {
        _scenarioLaunch.LaunchAsync(Arg.Any<ScenarioDefinition>(), Arg.Any<DateFilter?>(), Arg.Any<bool>())
            .Returns(new ScenarioLaunchResult(1, 0, 1)
            {
                ChannelOutcomes = [new ChannelOutcome("System", ChannelLaunchOutcome.AccessDenied)]
            });
        _scenarioQuery.GetSplashScenarios().Returns([Scenario("application-crashes", "Application crashes")]);

        var cut = Render<EmptyStateDashboard>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(ActiveDetailLaunch)));

        cut.Find(ActiveDetailLaunch).Click();

        cut.WaitForAssertion(() => _alertDialog.Received(1).ShowErrorAlert(
            "Launch scenario",
            Arg.Is<string>(message => message != null && message.Contains("access denied")),
            "Open from folder",
            Arg.Any<Func<Task>>()));
    }

    [Fact]
    public void DetailLaunch_WhenChannelAccessDenied_IsDisabledWithBlockedNote()
    {
        _channelReadinessService.GetReadinessAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                new ChannelReadiness("System", ChannelPresence.Present, ChannelEnablement.Enabled)
                {
                    Access = ChannelAccess.RequiresElevation
                }
            ]);
        _scenarioQuery.GetSplashScenarios().Returns([Scenario("application-crashes", "Application crashes")]);

        var cut = Render<EmptyStateDashboard>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(ActiveDetailLaunch)));

        Assert.Equal("true", cut.Find(ActiveDetailLaunch).GetAttribute("aria-disabled"));
        var blockedNote = cut.Find(".sidebar-tabs-tabpanel.active .scenario-detail__unavailable");
        Assert.Contains("blocked", blockedNote.TextContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DetailLaunch_WhenChannelAccessNotEvaluated_StaysLaunchable()
    {
        _channelReadinessService.GetReadinessAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                new ChannelReadiness("System", ChannelPresence.Present, ChannelEnablement.Enabled)
                {
                    Access = ChannelAccess.NotEvaluated
                }
            ]);
        _scenarioQuery.GetSplashScenarios().Returns([Scenario("application-crashes", "Application crashes")]);

        var cut = Render<EmptyStateDashboard>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(ActiveDetailLaunch)));

        Assert.Equal("false", cut.Find(ActiveDetailLaunch).GetAttribute("aria-disabled"));
        Assert.Empty(cut.FindAll(".sidebar-tabs-tabpanel.active .scenario-detail__unavailable"));
    }

    [Fact]
    public void DetailLaunch_WhenChannelNotOnHost_IsDisabledWithOfflineNote()
    {
        _channelReadinessService.GetReadinessAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([new ChannelReadiness("System", ChannelPresence.Absent, ChannelEnablement.Unknown)]);
        _scenarioQuery.GetSplashScenarios().Returns([Scenario("application-crashes", "Application crashes")]);

        var cut = Render<EmptyStateDashboard>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(ActiveDetailLaunch)));

        Assert.Equal("true", cut.Find(ActiveDetailLaunch).GetAttribute("aria-disabled"));
        var offlineNote = cut.Find(".sidebar-tabs-tabpanel.active .scenario-detail__unavailable");
        Assert.Contains("not on this computer", offlineNote.TextContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DetailLaunch_WhenLaunchOpensNothing_AnnouncesFailure()
    {
        _scenarioLaunch.LaunchAsync(Arg.Any<ScenarioDefinition>(), Arg.Any<DateFilter?>(), Arg.Any<bool>())
            .Returns(new ScenarioLaunchResult(0, 1, 0));
        _scenarioQuery.GetSplashScenarios().Returns([Scenario("application-crashes", "Application crashes")]);

        var cut = Render<EmptyStateDashboard>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(ActiveDetailLaunch)));

        cut.Find(ActiveDetailLaunch).Click();

        cut.WaitForAssertion(() =>
            _announcer.Received(1).Announce(Arg.Is<string>(message => message != null && message.Contains("No channels"))));
    }

    [Fact]
    public void DetailLaunch_WhenOneRequiredChannelMissing_IsDisabledWithOfflineNote()
    {
        _channelReadinessService.GetReadinessAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                new ChannelReadiness("System", ChannelPresence.Present, ChannelEnablement.Enabled)
                {
                    Access = ChannelAccess.Accessible
                }
            ]);
        _scenarioQuery.GetSplashScenarios().Returns(
        [
            Scenario("application-crashes", "Application crashes") with { Channels = ["System", "Missing"] }
        ]);

        var cut = Render<EmptyStateDashboard>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(ActiveDetailLaunch)));

        Assert.Equal("true", cut.Find(ActiveDetailLaunch).GetAttribute("aria-disabled"));
        var note = cut.Find(".sidebar-tabs-tabpanel.active .scenario-detail__unavailable");
        Assert.Contains("not on this computer", note.TextContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DetailLaunch_WhenPresenceUnknown_StaysLaunchable()
    {
        _channelReadinessService.GetReadinessAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([new ChannelReadiness("System", ChannelPresence.Unknown, ChannelEnablement.Unknown)]);
        _scenarioQuery.GetSplashScenarios().Returns([Scenario("application-crashes", "Application crashes")]);

        var cut = Render<EmptyStateDashboard>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(ActiveDetailLaunch)));

        var launch = cut.Find(ActiveDetailLaunch);
        Assert.Equal("false", launch.GetAttribute("aria-disabled"));
        Assert.Empty(cut.FindAll(".sidebar-tabs-tabpanel.active .scenario-detail__unavailable"));

        launch.Click();

        cut.WaitForAssertion(() => _scenarioLaunch.Received(1)
            .LaunchAsync(Arg.Is<ScenarioDefinition>(scenario => scenario != null && scenario.Id == "application-crashes"), null));
    }

    [Fact]
    public void DetailLaunch_WhenScenarioRequiresAdminButLivePresent_StaysLaunchable()
    {
        _scenarioQuery.GetSplashScenarios().Returns(
        [
            Scenario("security", "Security", requiresAdmin: true)
        ]);

        var cut = Render<EmptyStateDashboard>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(ActiveDetailLaunch)));

        Assert.Equal("false", cut.Find(ActiveDetailLaunch).GetAttribute("aria-disabled"));
    }

    [Fact]
    public void DetailOpenFromFolder_ByDefault_ScansSubfolders()
    {
        _scenarioLaunch.LaunchFromFolderAsync(Arg.Any<ScenarioDefinition>(), Arg.Any<DateFilter?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Func<ScenarioFolderPhase, Task>?>())
            .Returns(new ScenarioFolderLaunchResult { Outcome = ScenarioFolderOutcome.Completed, Opened = 1, Matched = 1 });
        _scenarioQuery.GetSplashScenarios().Returns([Scenario("application-crashes", "Application crashes")]);

        var cut = Render<EmptyStateDashboard>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(ActiveDetailOpenFolder)));
        cut.Find(ActiveDetailOpenFolder).Click();

        cut.WaitForAssertion(() => _scenarioLaunch.Received(1).LaunchFromFolderAsync(
            Arg.Any<ScenarioDefinition>(), Arg.Any<DateFilter?>(), true, Arg.Any<CancellationToken>(), Arg.Any<Func<ScenarioFolderPhase, Task>?>()));
    }

    [Fact]
    public void DetailOpenFromFolder_WhenCompleted_AnnouncesWithoutAlert()
    {
        _scenarioLaunch.LaunchFromFolderAsync(Arg.Any<ScenarioDefinition>(), Arg.Any<DateFilter?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Func<ScenarioFolderPhase, Task>?>())
            .Returns(new ScenarioFolderLaunchResult { Outcome = ScenarioFolderOutcome.Completed, Opened = 1, Matched = 1 });
        _scenarioQuery.GetSplashScenarios().Returns([Scenario("application-crashes", "Application crashes")]);

        var cut = Render<EmptyStateDashboard>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(ActiveDetailOpenFolder)));

        cut.Find(ActiveDetailOpenFolder).Click();

        cut.WaitForAssertion(() =>
        {
            _announcer.Received(1).Announce(Arg.Any<string>());
            _alertDialog.DidNotReceive().ShowAlert(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        });
    }

    [Fact]
    public void DetailOpenFromFolder_WhenError_ShowsErrorAlert()
    {
        _scenarioLaunch.LaunchFromFolderAsync(Arg.Any<ScenarioDefinition>(), Arg.Any<DateFilter?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Func<ScenarioFolderPhase, Task>?>())
            .Returns(ScenarioFolderLaunchResult.Error("access denied"));
        _scenarioQuery.GetSplashScenarios().Returns([Scenario("application-crashes", "Application crashes")]);

        var cut = Render<EmptyStateDashboard>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(ActiveDetailOpenFolder)));

        cut.Find(ActiveDetailOpenFolder).Click();

        cut.WaitForAssertion(() => _alertDialog.Received(1).ShowErrorAlert("Open from folder", "access denied"));
    }

    [Fact]
    public void DetailOpenFromFolder_WhenNoMatchingLogs_ShowsVisibleAlert()
    {
        _scenarioLaunch.LaunchFromFolderAsync(Arg.Any<ScenarioDefinition>(), Arg.Any<DateFilter?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Func<ScenarioFolderPhase, Task>?>())
            .Returns(new ScenarioFolderLaunchResult { Outcome = ScenarioFolderOutcome.NoMatchingLogs });
        _scenarioQuery.GetSplashScenarios().Returns([Scenario("application-crashes", "Application crashes")]);

        var cut = Render<EmptyStateDashboard>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(ActiveDetailOpenFolder)));

        cut.Find(ActiveDetailOpenFolder).Click();

        cut.WaitForAssertion(() => _alertDialog.Received(1).ShowAlert("Open from folder", Arg.Any<string>(), "OK"));
    }

    [Fact]
    public void DetailOpenFromFolder_WhenSubfoldersUnchecked_ScansTopLevelOnly()
    {
        _scenarioLaunch.LaunchFromFolderAsync(Arg.Any<ScenarioDefinition>(), Arg.Any<DateFilter?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Func<ScenarioFolderPhase, Task>?>())
            .Returns(new ScenarioFolderLaunchResult { Outcome = ScenarioFolderOutcome.Completed, Opened = 1, Matched = 1 });
        _scenarioQuery.GetSplashScenarios().Returns([Scenario("application-crashes", "Application crashes")]);

        var cut = Render<EmptyStateDashboard>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(ActiveDetailOpenFolder)));
        cut.Find(".sidebar-tabs-tabpanel.active .scenario-detail__subfolders input").Change(false);
        cut.Find(ActiveDetailOpenFolder).Click();

        cut.WaitForAssertion(() => _scenarioLaunch.Received(1).LaunchFromFolderAsync(
            Arg.Any<ScenarioDefinition>(), Arg.Any<DateFilter?>(), false, Arg.Any<CancellationToken>(), Arg.Any<Func<ScenarioFolderPhase, Task>?>()));
    }

    [Fact]
    public void DetailReadiness_WhenChannelDisabled_ShowsDisabledStatus()
    {
        _channelReadinessService.GetReadinessAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([new ChannelReadiness("System", ChannelPresence.Present, ChannelEnablement.Disabled)]);
        _scenarioQuery.GetSplashScenarios().Returns([Scenario("application-crashes", "Application crashes")]);

        var cut = Render<EmptyStateDashboard>();

        cut.WaitForAssertion(() =>
            Assert.Contains("Disabled", cut.Find(".sidebar-tabs-tabpanel.active .scenario-detail__channel-readiness").TextContent));
    }

    [Fact]
    public void DetailReadiness_WhenChannelRequiresElevation_ShowsAccessStatus()
    {
        _channelReadinessService.GetReadinessAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                new ChannelReadiness("System", ChannelPresence.Present, ChannelEnablement.Enabled)
                {
                    Access = ChannelAccess.RequiresElevation
                }
            ]);
        _scenarioQuery.GetSplashScenarios().Returns([Scenario("application-crashes", "Application crashes")]);

        var cut = Render<EmptyStateDashboard>();

        cut.WaitForAssertion(() =>
            Assert.Contains(
                "Access denied (Needs elevation)",
                cut.Find(".sidebar-tabs-tabpanel.active .scenario-detail__channel-readiness").TextContent));
    }

    [Fact]
    public void DetailStar_TogglesFavorite()
    {
        _scenarioQuery.GetSplashScenarios().Returns([Scenario("application-crashes", "Application crashes")]);

        var cut = Render<EmptyStateDashboard>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(ActiveDetailStar)));

        cut.Find(ActiveDetailStar).Click();

        cut.WaitForAssertion(() =>
            _favoriteCommands.Received(1).SetFavorite("application-crashes", "Application crashes", true));
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_DoesNotThrow()
    {
        var cut = Render<EmptyStateDashboard>();

        await cut.Instance.DisposeAsync();
        await cut.Instance.DisposeAsync();
    }

    [Fact]
    public void EnableChannel_WhenConfirmCancelled_DoesNotInvokeService()
    {
        _channelEnable.CanEnable.Returns(true);
        _alertDialog.ShowAlert(Arg.Any<string>(), Arg.Any<string>(), "Enable", "Cancel").Returns(false);
        _channelReadinessService.GetReadinessAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([new ChannelReadiness("Microsoft-Windows-Sample/Operational", ChannelPresence.Present, ChannelEnablement.Disabled)]);
        _scenarioQuery.GetSplashScenarios()
            .Returns([Scenario("test", "Test") with { Channels = ["Microsoft-Windows-Sample/Operational"] }]);

        var cut = Render<EmptyStateDashboard>();
        cut.WaitForAssertion(() =>
            Assert.NotEmpty(cut.FindAll(".sidebar-tabs-tabpanel.active .scenario-detail__enable")));
        cut.Find(".sidebar-tabs-tabpanel.active .scenario-detail__enable").Click();

        _channelEnable.DidNotReceive().EnableAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void EnableChannel_WhenConfirmedAndEnabled_InvokesServiceAndRefreshesReadiness()
    {
        _channelEnable.CanEnable.Returns(true);
        _channelEnable.EnableAsync("Microsoft-Windows-Sample/Operational", Arg.Any<CancellationToken>())
            .Returns(new ChannelEnableResult(ChannelEnableOutcome.Enabled, 0));
        _alertDialog.ShowAlert(Arg.Any<string>(), Arg.Any<string>(), "Enable", "Cancel").Returns(true);
        _channelReadinessService.GetReadinessAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(
                [new ChannelReadiness("Microsoft-Windows-Sample/Operational", ChannelPresence.Present, ChannelEnablement.Disabled)],
                [new ChannelReadiness("Microsoft-Windows-Sample/Operational", ChannelPresence.Present, ChannelEnablement.Enabled)]);
        _scenarioQuery.GetSplashScenarios()
            .Returns([Scenario("test", "Test") with { Channels = ["Microsoft-Windows-Sample/Operational"] }]);

        var cut = Render<EmptyStateDashboard>();
        cut.WaitForAssertion(() =>
            Assert.NotEmpty(cut.FindAll(".sidebar-tabs-tabpanel.active .scenario-detail__enable")));
        cut.Find(".sidebar-tabs-tabpanel.active .scenario-detail__enable").Click();

        cut.WaitForAssertion(() =>
            _channelEnable.Received(1).EnableAsync("Microsoft-Windows-Sample/Operational", Arg.Any<CancellationToken>()));
        cut.WaitForAssertion(() =>
            Assert.Empty(cut.FindAll(".sidebar-tabs-tabpanel.active .scenario-detail__enable")));
    }

    [Fact]
    public void Favorites_LoadDispatchedOnFirstRender()
    {
        var cut = Render<EmptyStateDashboard>();

        cut.WaitForAssertion(() => _favoriteCommands.Received(1).Load());
    }

    [Fact]
    public void Favoriting_ReactivelyAddsFavoritesTab()
    {
        _scenarioQuery.GetSplashScenarios().Returns([Scenario("application-crashes", "Application crashes")]);

        var cut = Render<EmptyStateDashboard>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[role='tab']")));
        Assert.DoesNotContain("Favorites", TabLabels(cut));

        _favoritesSource.FavoriteScenarioIds.Returns(ImmutableHashSet.Create("application-crashes"));
        RaiseFavoritesChanged(cut);

        cut.WaitForAssertion(() => Assert.Contains("Favorites", TabLabels(cut)));
    }

    [Fact]
    public void Favoriting_ReactivelyFillsTheActiveDetailStar()
    {
        _scenarioQuery.GetSplashScenarios().Returns([Scenario("application-crashes", "Application crashes")]);

        var cut = Render<EmptyStateDashboard>();
        cut.WaitForAssertion(() => Assert.Equal("false", cut.Find(ActiveDetailStar).GetAttribute("aria-pressed")));

        _favoritesSource.FavoriteScenarioIds.Returns(ImmutableHashSet.Create("application-crashes"));
        RaiseFavoritesChanged(cut);

        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find(ActiveDetailStar).GetAttribute("aria-pressed")));
    }

    [Fact]
    public void FolderScan_WhenCancelClicked_CancelsTokenAndShowsCancellingWithoutDialog()
    {
        var release = new TaskCompletionSource<ScenarioFolderLaunchResult>();
        CancellationToken captured = default;
        _scenarioLaunch
            .LaunchFromFolderAsync(Arg.Any<ScenarioDefinition>(), Arg.Any<DateFilter?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Func<ScenarioFolderPhase, Task>?>())
            .Returns(async ci =>
            {
                captured = ci.Arg<CancellationToken>();
                await ci.Arg<Func<ScenarioFolderPhase, Task>>()!(ScenarioFolderPhase.Scanning);

                return await release.Task;
            });
        _scenarioQuery.GetSplashScenarios().Returns([Scenario("application-crashes", "Application crashes")]);

        var cut = Render<EmptyStateDashboard>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(ActiveDetailOpenFolder)));
        cut.Find(ActiveDetailOpenFolder).Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".empty-dashboard__chip--scanning button")));

        cut.Find(".empty-dashboard__chip--scanning button").Click();

        cut.WaitForAssertion(() =>
        {
            var chip = cut.Find(".empty-dashboard__chip--scanning");
            Assert.Contains("Cancelling", chip.TextContent);
            Assert.Empty(cut.FindAll(".empty-dashboard__chip--scanning button"));
        });
        Assert.True(captured.IsCancellationRequested);

        release.SetResult(ScenarioFolderLaunchResult.Cancelled);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".empty-dashboard__chip--scanning")));
        _alertDialog.DidNotReceive().ShowAlert(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        _alertDialog.DidNotReceive().ShowErrorAlert("Open from folder", Arg.Any<string>());
    }

    [Fact]
    public async Task FolderScan_WhenLaunchedFromReactiveBannerRetry_GuardsScanAndReenablesButtons()
    {
        _scenarioLaunch.LaunchAsync(Arg.Any<ScenarioDefinition>(), Arg.Any<DateFilter?>(), Arg.Any<bool>())
            .Returns(new ScenarioLaunchResult(0, 0, 1)
            {
                ChannelOutcomes = [new ChannelOutcome("System", ChannelLaunchOutcome.NotPresent)]
            });

        Func<Task>? retry = null;
        _alertDialog.ShowErrorAlert("Launch scenario", Arg.Any<string>(), "Open from folder", Arg.Any<Func<Task>?>())
            .Returns(call =>
            {
                retry = call.Arg<Func<Task>>();

                return Task.CompletedTask;
            });

        var pickerGate = new TaskCompletionSource();
        var release = new TaskCompletionSource<ScenarioFolderLaunchResult>();
        _scenarioLaunch
            .LaunchFromFolderAsync(Arg.Any<ScenarioDefinition>(), Arg.Any<DateFilter?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Func<ScenarioFolderPhase, Task>?>())
            .Returns(async ci =>
            {
                await pickerGate.Task;
                await ci.Arg<Func<ScenarioFolderPhase, Task>>()!(ScenarioFolderPhase.Scanning);

                return await release.Task;
            });
        _scenarioQuery.GetSplashScenarios().Returns([Scenario("application-crashes", "Application crashes")]);

        var cut = Render<EmptyStateDashboard>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(ActiveDetailLaunch)));

        cut.Find(ActiveDetailLaunch).Click();
        cut.WaitForAssertion(() => Assert.NotNull(retry));

        var retried = cut.InvokeAsync(() => retry!());

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll(".empty-dashboard__chip--scanning"));
            Assert.True(cut.Find(ActiveDetailOpenFolder).HasAttribute("disabled"));
        });

        pickerGate.SetResult();
        cut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(cut.FindAll(".empty-dashboard__chip--scanning"));
            Assert.True(cut.Find(ActiveDetailOpenFolder).HasAttribute("disabled"));
        });

        release.SetResult(new ScenarioFolderLaunchResult { Outcome = ScenarioFolderOutcome.NoMatchingLogs });
        await retried;

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll(".empty-dashboard__chip--scanning"));
            Assert.False(cut.Find(ActiveDetailOpenFolder).HasAttribute("disabled"));
        });
    }

    [Fact]
    public void FolderScan_WhenOpeningPhase_RelabelsAndDropsCancelButton()
    {
        var release = new TaskCompletionSource<ScenarioFolderLaunchResult>();
        _scenarioLaunch
            .LaunchFromFolderAsync(Arg.Any<ScenarioDefinition>(), Arg.Any<DateFilter?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Func<ScenarioFolderPhase, Task>?>())
            .Returns(async ci =>
            {
                var onPhase = ci.Arg<Func<ScenarioFolderPhase, Task>>()!;
                await onPhase(ScenarioFolderPhase.Scanning);
                await onPhase(ScenarioFolderPhase.Opening);

                return await release.Task;
            });
        _scenarioQuery.GetSplashScenarios().Returns([Scenario("application-crashes", "Application crashes")]);

        var cut = Render<EmptyStateDashboard>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(ActiveDetailOpenFolder)));
        cut.Find(ActiveDetailOpenFolder).Click();

        cut.WaitForAssertion(() =>
        {
            var chip = cut.Find(".empty-dashboard__chip--scanning");
            Assert.Contains("Opening logs", chip.TextContent);
            Assert.Empty(cut.FindAll(".empty-dashboard__chip--scanning button"));
        });

        release.SetResult(new ScenarioFolderLaunchResult { Outcome = ScenarioFolderOutcome.Completed, Opened = 1, Matched = 1 });
    }

    [Fact]
    public void FolderScan_WhenScanning_ShowsMastheadCancelChip()
    {
        var release = new TaskCompletionSource<ScenarioFolderLaunchResult>();
        _scenarioLaunch
            .LaunchFromFolderAsync(Arg.Any<ScenarioDefinition>(), Arg.Any<DateFilter?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Func<ScenarioFolderPhase, Task>?>())
            .Returns(async ci =>
            {
                await ci.Arg<Func<ScenarioFolderPhase, Task>>()!(ScenarioFolderPhase.Scanning);

                return await release.Task;
            });
        _scenarioQuery.GetSplashScenarios().Returns([Scenario("application-crashes", "Application crashes")]);

        var cut = Render<EmptyStateDashboard>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(ActiveDetailOpenFolder)));
        Assert.Empty(cut.FindAll(".empty-dashboard__chip--scanning"));

        cut.Find(ActiveDetailOpenFolder).Click();

        cut.WaitForAssertion(() =>
        {
            var chip = cut.Find(".empty-dashboard__chip--scanning");
            Assert.Contains("Scanning", chip.TextContent);
            Assert.Contains("Application crashes", chip.TextContent);
            Assert.NotEmpty(cut.FindAll(".empty-dashboard__chip--scanning button"));
        });

        release.SetResult(ScenarioFolderLaunchResult.Cancelled);
    }

    [Fact]
    public void FolderScan_WhenUserReselectsScenario_CancelChipRemainsInMasthead()
    {
        var release = new TaskCompletionSource<ScenarioFolderLaunchResult>();
        _scenarioLaunch
            .LaunchFromFolderAsync(Arg.Any<ScenarioDefinition>(), Arg.Any<DateFilter?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Func<ScenarioFolderPhase, Task>?>())
            .Returns(async ci =>
            {
                await ci.Arg<Func<ScenarioFolderPhase, Task>>()!(ScenarioFolderPhase.Scanning);

                return await release.Task;
            });
        _scenarioQuery.GetSplashScenarios().Returns(
        [
            Scenario("crash-a", "Crash A", group: ScenarioGroup.SqlServer, order: 0),
            Scenario("crash-b", "Crash B", group: ScenarioGroup.SqlServer, order: 1)
        ]);

        var cut = Render<EmptyStateDashboard>();
        cut.WaitForAssertion(() => Assert.Equal("Crash A", cut.Find(ActiveDetailName).TextContent));

        cut.Find(ActiveDetailOpenFolder).Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".empty-dashboard__chip--scanning")));

        cut.FindAll(ActiveOption).First(option => option.TextContent.Contains("Crash B")).Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Crash B", cut.Find(ActiveDetailName).TextContent);
            Assert.NotEmpty(cut.FindAll(".empty-dashboard__chip--scanning"));
        });

        release.SetResult(ScenarioFolderLaunchResult.Cancelled);
    }

    [Fact]
    public void ManageDatabases_IsInMastheadAndInvokesOpen()
    {
        var cut = Render<EmptyStateDashboard>();

        cut.Find(".empty-dashboard__utility").Click();

        cut.WaitForAssertion(() => _actions.Received(1).OpenDatabaseToolsAsync());
        Assert.DoesNotContain(
            cut.FindAll(".empty-dashboard__launchbar button"),
            button => button.TextContent.Contains("Manage databases"));
    }

    [Fact]
    public void Masthead_UsesJournalBrandIcon()
    {
        var cut = Render<EmptyStateDashboard>();

        Assert.NotEmpty(cut.FindAll(".empty-dashboard__brand-mark .bi-journal-text"));
    }

    [Fact]
    public void OpenFolder_ByDefault_ScansSubfolders()
    {
        _actions.OpenFolderAsync(Arg.Is(false), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Func<FolderOpenPhase, Task>?>())
            .Returns(Task.CompletedTask);

        var cut = Render<EmptyStateDashboard>();
        FindLaunch(cut, "Open Folder...").Click();

        cut.WaitForAssertion(() => _actions.Received(1).OpenFolderAsync(
            false, true, Arg.Any<CancellationToken>(), Arg.Any<Func<FolderOpenPhase, Task>?>()));
    }

    [Fact]
    public void OpenFolder_WhenCancelClicked_CancelsTokenAndShowsCancelling()
    {
        var release = new TaskCompletionSource();
        CancellationToken captured = default;
        _actions.OpenFolderAsync(Arg.Is(false), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Func<FolderOpenPhase, Task>?>())
            .Returns(async ci =>
            {
                captured = ci.Arg<CancellationToken>();
                await ci.Arg<Func<FolderOpenPhase, Task>>()!(FolderOpenPhase.Scanning);
                await release.Task;
            });

        var cut = Render<EmptyStateDashboard>();
        FindLaunch(cut, "Open Folder...").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".empty-dashboard__chip--scanning button")));

        cut.Find(".empty-dashboard__chip--scanning button").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Cancelling", cut.Find(".empty-dashboard__chip--scanning").TextContent);
            Assert.Empty(cut.FindAll(".empty-dashboard__chip--scanning button"));
        });
        Assert.True(captured.IsCancellationRequested);

        release.SetResult();
    }

    [Fact]
    public void OpenFolder_WhenOpeningPhase_RelabelsAndDropsCancel()
    {
        var release = new TaskCompletionSource();
        _actions.OpenFolderAsync(Arg.Is(false), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Func<FolderOpenPhase, Task>?>())
            .Returns(async ci =>
            {
                var onPhase = ci.Arg<Func<FolderOpenPhase, Task>>()!;
                await onPhase(FolderOpenPhase.Scanning);
                await onPhase(FolderOpenPhase.Opening);
                await release.Task;
            });

        var cut = Render<EmptyStateDashboard>();
        FindLaunch(cut, "Open Folder...").Click();

        cut.WaitForAssertion(() =>
        {
            var chip = cut.Find(".empty-dashboard__chip--scanning");
            Assert.Contains("Opening logs", chip.TextContent);
            Assert.Empty(cut.FindAll(".empty-dashboard__chip--scanning button"));
        });

        release.SetResult();
    }

    [Fact]
    public void OpenFolder_WhenScanning_ShowsGenericScanChipWithCancel()
    {
        var release = new TaskCompletionSource();
        _actions.OpenFolderAsync(Arg.Is(false), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Func<FolderOpenPhase, Task>?>())
            .Returns(async ci =>
            {
                await ci.Arg<Func<FolderOpenPhase, Task>>()!(FolderOpenPhase.Scanning);
                await release.Task;
            });

        var cut = Render<EmptyStateDashboard>();
        FindLaunch(cut, "Open Folder...").Click();

        cut.WaitForAssertion(() =>
        {
            var chip = cut.Find(".empty-dashboard__chip--scanning");
            Assert.Contains("Scanning folder for logs", chip.TextContent);
            Assert.NotEmpty(cut.FindAll(".empty-dashboard__chip--scanning button"));
        });

        release.SetResult();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".empty-dashboard__chip--scanning")));
    }

    [Fact]
    public void OpenFolder_WhenSubfoldersUnchecked_ScansTopLevelOnly()
    {
        _actions.OpenFolderAsync(Arg.Is(false), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Func<FolderOpenPhase, Task>?>())
            .Returns(Task.CompletedTask);

        var cut = Render<EmptyStateDashboard>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".empty-dashboard__empty")));
        cut.Find(".empty-dashboard__subfolders input").Change(false);
        FindLaunch(cut, "Open Folder...").Click();

        cut.WaitForAssertion(() => _actions.Received(1).OpenFolderAsync(
            false, false, Arg.Any<CancellationToken>(), Arg.Any<Func<FolderOpenPhase, Task>?>()));
    }

    [Fact]
    public void QuickLaunch_OpenApplicationAndSystem_InvokesCombineOpen()
    {
        var cut = Render<EmptyStateDashboard>();

        FindLaunch(cut, "Open Application + System (live)").Click();

        cut.WaitForAssertion(() => _actions.Received(1).OpenLiveLogsAsync(
            Arg.Is<IEnumerable<string>>(channels => channels != null && channels.SequenceEqual(new[] { "Application", "System" })),
            false,
            showInlineAlerts: true));
    }

    [Fact]
    public void QuickLaunch_OpenFile_InvokesOpenFile()
    {
        var cut = Render<EmptyStateDashboard>();

        FindLaunch(cut, "Open Log file...").Click();

        cut.WaitForAssertion(() => _actions.Received(1).OpenFileAsync(false));
    }

    [Fact]
    public void QuickLaunch_VisibleLabelsDropOpenButAriaLabelKeepsIt()
    {
        var cut = Render<EmptyStateDashboard>();

        var primary = FindLaunch(cut, "Open Application + System (live)");

        Assert.Equal("Application + System (live)", primary.TextContent.Trim());
        Assert.Equal("Open Application + System (live)", primary.GetAttribute("aria-label"));
        Assert.DoesNotContain("Open", primary.TextContent);
    }

    [Fact]
    public void QuickLaunch_WhileBusy_DisablesOtherButtons()
    {
        var gate = new TaskCompletionSource();
        _actions.OpenFileAsync(false).Returns(gate.Task);

        var cut = Render<EmptyStateDashboard>();
        FindLaunch(cut, "Open Log file...").Click();

        cut.WaitForAssertion(() => Assert.True(FindLaunch(cut, "Open Application (live)").HasAttribute("disabled")));

        gate.SetResult();
    }

    [Fact]
    public void Security_WhenAccessDenied_IsAriaDisabledWithReason()
    {
        _channelReadinessService.GetReadinessAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                new ChannelReadiness("Security", ChannelPresence.Present, ChannelEnablement.Enabled)
                {
                    Access = ChannelAccess.RequiresElevation
                }
            ]);

        var cut = Render<EmptyStateDashboard>();

        cut.WaitForAssertion(() =>
        {
            var security = FindLaunch(cut, "Open Security (live)");
            Assert.Equal("true", security.GetAttribute("aria-disabled"));
            var reasonId = security.GetAttribute("aria-describedby");
            Assert.False(string.IsNullOrEmpty(reasonId));
            Assert.Contains("Access denied", cut.Find($"#{reasonId}").TextContent, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Security_WhenAccessible_InvokesOpen()
    {
        _channelReadinessService.GetReadinessAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                new ChannelReadiness("Security", ChannelPresence.Present, ChannelEnablement.Enabled)
                {
                    Access = ChannelAccess.Accessible
                }
            ]);

        var cut = Render<EmptyStateDashboard>();
        FindLaunch(cut, "Open Security (live)").Click();

        cut.WaitForAssertion(() => _actions.Received(1).OpenLiveLogAsync("Security", false));
    }

    [Fact]
    public void SelectingCategory_ShowsThatCategorysScenarios()
    {
        _scenarioQuery.GetSplashScenarios().Returns(
        [
            Scenario("application-crashes", "Application crashes", group: ScenarioGroup.SystemHealth),
            Scenario("sql-health-triage", "SQL Server health triage", group: ScenarioGroup.SqlServer)
        ]);

        var cut = Render<EmptyStateDashboard>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[role='tab']")));

        cut.FindAll("[role='tab']").First(tab => tab.TextContent.Contains("SQL Server")).Click();

        cut.WaitForAssertion(() =>
        {
            var names = ActiveOptionNames(cut);
            Assert.Equal(["SQL Server health triage"], names);
        });
    }

    [Fact]
    public void SelectingScenario_UpdatesDetail()
    {
        _scenarioQuery.GetSplashScenarios().Returns(
        [
            Scenario("crash-a", "Crash A", group: ScenarioGroup.SqlServer, order: 0),
            Scenario("crash-b", "Crash B", group: ScenarioGroup.SqlServer, order: 1)
        ]);

        var cut = Render<EmptyStateDashboard>();
        cut.WaitForAssertion(() => Assert.Equal("Crash A", cut.Find(ActiveDetailName).TextContent));

        cut.FindAll(ActiveOption).First(option => option.TextContent.Contains("Crash B")).Click();

        cut.WaitForAssertion(() => Assert.Equal("Crash B", cut.Find(ActiveDetailName).TextContent));
    }

    [Fact]
    public void StarterScenarioIds_AllResolveInBuiltInCatalog()
    {
        var registry = new BuiltInScenarioRegistry([new BuiltInScenarioSource()]);
        var catalogIds = registry.Scenarios.Select(scenario => scenario.Id).ToHashSet(StringComparer.Ordinal);

        Assert.All(EmptyStateDashboard.StarterScenarioIds, id => Assert.Contains(id, catalogIds));
    }

    [Fact]
    public void Tablist_FavoritesAbsentWhenNoneFavorited()
    {
        _scenarioQuery.GetSplashScenarios().Returns([Scenario("application-crashes", "Application crashes")]);

        var cut = Render<EmptyStateDashboard>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[role='tab']")));

        Assert.DoesNotContain("Favorites", TabLabels(cut));
    }

    [Fact]
    public void Tablist_ListsFavoritesRecommendedAndGroupsInOrder()
    {
        _favoritesSource.FavoriteScenarioIds.Returns(ImmutableHashSet.Create("application-crashes"));
        _scenarioQuery.GetSplashScenarios().Returns(
        [
            Scenario("application-crashes", "Application crashes", group: ScenarioGroup.SystemHealth),
            Scenario("sql-health-triage", "SQL Server health triage", group: ScenarioGroup.SqlServer)
        ]);

        var cut = Render<EmptyStateDashboard>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[role='tab']")));

        Assert.Equal(
            ["Favorites", "Recommended", "System Health", "SQL Server"],
            TabLabels(cut));
    }

    [Fact]
    public void ToSplashCategory_MapsAllScenarioGroups()
    {
        Assert.All(
            Enum.GetValues<ScenarioGroup>(),
            group => Assert.NotNull(SplashCategoryMapping.ToSplashCategory(group)));
    }

    [Fact]
    public void Unfavoriting_LastFavorite_RemovesTabAndReconcilesActive()
    {
        _favoritesSource.FavoriteScenarioIds.Returns(ImmutableHashSet.Create("application-crashes"));
        _scenarioQuery.GetSplashScenarios().Returns([Scenario("application-crashes", "Application crashes")]);

        var cut = Render<EmptyStateDashboard>();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Favorites", TabLabels(cut));
            Assert.Equal("Favorites", cut.Find("[role='tab'][aria-selected='true']").TextContent.Trim());
        });

        _favoritesSource.FavoriteScenarioIds.Returns(ImmutableHashSet<string>.Empty);
        RaiseFavoritesChanged(cut);

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Favorites", TabLabels(cut));
            Assert.Equal("Recommended", cut.Find("[role='tab'][aria-selected='true']").TextContent.Trim());
        });
    }

    private static IReadOnlyList<string> ActiveOptionNames(IRenderedComponent<EmptyStateDashboard> cut) =>
        cut.FindAll(".sidebar-tabs-tabpanel.active .scenario-browser__option-name").Select(node => node.TextContent).ToList();

    private static IElement FindLaunch(IRenderedComponent<EmptyStateDashboard> cut, string ariaLabel) =>
        cut.FindAll(".empty-dashboard__launch").First(button => button.GetAttribute("aria-label") == ariaLabel);

    private static ScenarioDefinition Scenario(
        string id,
        string name,
        bool requiresAdmin = false,
        ScenarioGroup group = ScenarioGroup.SystemHealth,
        int order = 0) =>
        new()
        {
            Id = id,
            Name = name,
            Purpose = "Purpose",
            Group = group,
            Channels = ["System"],
            RequiresAdmin = requiresAdmin,
            Filters = [],
            Order = order
        };

    private static IReadOnlyList<string> TabLabels(IRenderedComponent<EmptyStateDashboard> cut) =>
        cut.FindAll("[role='tab']").Select(tab => tab.TextContent.Trim()).ToList();

    private void RaiseFavoritesChanged(IRenderedComponent<EmptyStateDashboard> cut) =>
        cut.InvokeAsync(() => _favoritesSource.Changed += Raise.Event<Action>());
}
