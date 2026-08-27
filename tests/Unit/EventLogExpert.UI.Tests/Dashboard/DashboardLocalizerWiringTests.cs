// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Localization;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Scenarios;
using EventLogExpert.Runtime.Scenarios.Favorites;
using EventLogExpert.Scenarios.Catalog;
using EventLogExpert.UI.Dashboard;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.Dashboard;

public sealed class DashboardLocalizerWiringTests : BunitContext
{
    private const string ActiveDetailOpenFolder = ".sidebar-tabs-tabpanel.active .scenario-detail__open-folder";

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

    public DashboardLocalizerWiringTests()
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
        Services.AddEventLogLocalization();
        Services.AddSingleton<IStringLocalizer<SharedResource>>(new MarkerLocalizer());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void EmptyStateDashboard_QuickLaunchTextAndNestedAria_AreDrivenByTheLocalizer()
    {
        var cut = Render<EmptyStateDashboard>();

        Assert.Contains("[[Dashboard_QuickLaunch]]", cut.Markup);
        Assert.Contains("[[Dashboard_ManageDatabases]]", cut.Markup);

        var primary = cut.Find(".empty-dashboard__launch--primary");
        Assert.Equal("[[Dashboard_OpenAria([[Dashboard_Open_ApplicationSystem]])]]", primary.GetAttribute("aria-label"));
        Assert.Equal("[[Dashboard_Open_ApplicationSystem]]", primary.QuerySelector("span")!.TextContent.Trim());
    }

    [Fact]
    public void ScenarioBrowserPanel_EmptyStateAndListAria_AreDrivenByTheLocalizer()
    {
        var empty = Render<ScenarioBrowserPanel>(parameters => parameters
            .Add(panel => panel.Scenarios, Array.Empty<ScenarioDefinition>()));

        Assert.Contains("[[Dashboard_NoScenariosInCategory]]", empty.Markup);

        var populated = Render<ScenarioBrowserPanel>(parameters => parameters
            .Add(panel => panel.Scenarios, [Scenario("application-crashes", "Application crashes")])
            .Add(panel => panel.IsFavored, _ => false)
            .Add(panel => panel.IsScenarioDisabled, _ => false));

        Assert.Equal("[[Dashboard_ScenariosListAria]]", populated.Find("ul[role='listbox']").GetAttribute("aria-label"));
    }

    [Fact]
    public void ScenarioDetail_LabelsAndFavoriteAria_AreDrivenByTheLocalizerWhileCatalogDataStaysVerbatim()
    {
        var scenario = Scenario("sentinel", "Zzz Sentinel Scenario") with
        {
            Purpose = "Zzz Sentinel Purpose",
            Channels = ["Zzz-Sentinel-Channel"]
        };

        var cut = Render<ScenarioDetail>(parameters => parameters
            .Add(detail => detail.Scenario, scenario));

        var markup = cut.Markup;

        Assert.Contains("Zzz Sentinel Scenario", markup);
        Assert.Contains("Zzz Sentinel Purpose", markup);
        Assert.Contains("Zzz-Sentinel-Channel", markup);
        Assert.Contains("[[Dashboard_LogsLabel]]", markup);
        Assert.Contains("[[Dashboard_Launch]]", markup);
        Assert.Contains("[[Dashboard_OpenFromFolder]]", markup);
        Assert.Contains("[[Dashboard_IncludeSubfolders]]", markup);
        Assert.Contains("[[Dashboard_AddFavoriteAria(", markup);
        Assert.Contains("Zzz Sentinel Scenario", cut.Find(".scenario-detail__star").GetAttribute("aria-label"));
    }

    [Fact]
    public async Task ScenarioFolderScanStatus_UsesParameterizedKeyWithScenarioNameData()
    {
        var release = new TaskCompletionSource<ScenarioFolderLaunchResult>();
        _scenarioLaunch
            .LaunchFromFolderAsync(Arg.Any<ScenarioDefinition>(), Arg.Any<DateFilter?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Func<ScenarioFolderPhase, Task>?>())
            .Returns(async callInfo =>
            {
                await callInfo.Arg<Func<ScenarioFolderPhase, Task>>()!(ScenarioFolderPhase.Scanning);

                return await release.Task;
            });
        _scenarioQuery.GetSplashScenarios().Returns([Scenario("sentinel", "Zzz Sentinel Scenario")]);

        var cut = Render<EmptyStateDashboard>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(ActiveDetailOpenFolder)));
        var pendingClick = cut.Find(ActiveDetailOpenFolder).ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal(
            "[[Dashboard_ScanStatus_ScanningLabeled(Zzz Sentinel Scenario)]]",
            cut.Find(".empty-dashboard__chip--scanning .empty-dashboard__chip-text").TextContent.Trim()));

        release.SetResult(ScenarioFolderLaunchResult.Cancelled);
        await pendingClick;
    }

    private static ScenarioDefinition Scenario(string id, string name) =>
        new()
        {
            Id = id,
            Name = name,
            Purpose = "Purpose",
            Group = ScenarioGroup.SystemHealth,
            Channels = ["System"],
            Filters = []
        };
}
