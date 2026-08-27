// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Localization;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Scenarios;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.Menu;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.Menu;

/// <summary>
///     Proves the Menu bar actually CONSULTS the localizer (rather than emitting a reverted hardcoded English
///     literal) by substituting a marker localizer that returns <c>[[key]]</c>. A label wired to <c>Localizer[...]</c>
///     renders the marker; a data value (channel name) renders verbatim. Byte-identity of real copy is proven by PR-review
///     resource diffs, not test pins.
/// </summary>
public sealed class MenuBarLocalizerWiringTests : BunitContext
{
    private readonly IMenuActionService _actions = Substitute.For<IMenuActionService>();
    private readonly IAlertDialogService _alertDialogService = Substitute.For<IAlertDialogService>();
    private readonly IEventLogQueries _eventLogQueries = Substitute.For<IEventLogQueries>();
    private readonly IFilterPaneQueries _filterPaneQueries = Substitute.For<IFilterPaneQueries>();
    private readonly IHistogramVisibilitySource _histogramVisibility = Substitute.For<IHistogramVisibilitySource>();
    private readonly ILogTableQueries _logTableQueries = Substitute.For<ILogTableQueries>();
    private readonly IMenuService _menuService = Substitute.For<IMenuService>();
    private readonly IChannelReadinessService _readinessService = Substitute.For<IChannelReadinessService>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();

    public MenuBarLocalizerWiringTests()
    {
        Services.AddSingleton(_actions);
        Services.AddSingleton(_alertDialogService);
        Services.AddSingleton(_eventLogQueries);
        Services.AddSingleton(_filterPaneQueries);
        Services.AddSingleton(_histogramVisibility);
        Services.AddSingleton(_logTableQueries);
        Services.AddSingleton(_menuService);
        Services.AddSingleton(_readinessService);
        Services.AddSingleton(_settings);
        Services.AddSingleton<IStringLocalizer<SharedResource>>(new MarkerLocalizer());

        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/EventLogExpert.UI/Menu/MenuAnchor.js")
            .Setup<MenuAnchorRect>("getMenuElementRect", _ => true)
            .SetResult(new MenuAnchorRect(0, 0, 0, 0, 0, 0));
        _readinessService.GetReadinessAsync(Arg.Any<CancellationToken>()).Returns([]);
        _readinessService.GetReadinessAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => ReadinessFor(call.Arg<IEnumerable<string>>()!));
    }

    [Fact]
    public async Task ItemLabelsDisabledReasonAndStatus_AreDrivenByTheLocalizer_WhileChannelNamesStayVerbatim()
    {
        _readinessService.GetReadinessAsync(Arg.Any<CancellationToken>())
            .Returns(
            [
                new ChannelReadiness(LogChannelNames.ApplicationLog, ChannelPresence.Present, ChannelEnablement.Enabled)
                {
                    Access = ChannelAccess.RequiresElevation
                }
            ]);
        _logTableQueries.IsGrouping().Returns(false);

        var fileItems = await OpenMenu("[[Menu_File]]");
        Assert.Contains(fileItems, item => item.Label == "[[Menu_File_Exit]]");

        var viewItems = await OpenMenu("[[Menu_View]]");
        var groupDescending = viewItems.Single(item => item.Label == "[[Menu_View_GroupDescending]]");
        Assert.Equal("[[Menu_GroupDisabledReason]]", groupDescending.DisabledReason);

        var openItems = fileItems.Single(item => item.Label == "[[Menu_File_Open]]").Children!;
        var liveItems = openItems.Single(item => item.Label == "[[Menu_Open_Live]]").Children!;
        var application = liveItems.Single(item => item.Label == LogChannelNames.ApplicationLog);

        Assert.Equal("[[Menu_Status_Elevate]]", application.StatusText);
        // The channel name is data: had it been routed through the localizer it would render "[[Application]]".
        Assert.Equal(LogChannelNames.ApplicationLog, application.Label);
    }

    [Fact]
    public async Task OtherLogsTree_FolderAndLeafNames_StayVerbatim_UnderMarkerLocalizer()
    {
        const string sentinelChannel = "Microsoft-Windows-ZzzSentinelFolder/ZzzSentinelLeaf";
        _readinessService.GetReadinessAsync(Arg.Any<CancellationToken>())
            .Returns([new ChannelReadiness(sentinelChannel, ChannelPresence.Present, ChannelEnablement.Unknown)]);

        var fileItems = await OpenMenu("[[Menu_File]]");
        var openItems = fileItems.Single(item => item.Label == "[[Menu_File_Open]]").Children!;
        var liveItems = openItems.Single(item => item.Label == "[[Menu_Open_Live]]").Children!;
        var otherLogs = liveItems.Single(item => item.ChildrenLoader is not null);
        var tree = await otherLogs.ChildrenLoader!();

        var labels = new List<string>();
        CollectLabels(tree, labels);

        // Under the marker localizer a localized string renders "[[key]]"; folder/leaf names staying raw proves
        // BuildOtherLogsTree never routes dynamic channel data through the localizer.
        Assert.Contains("Microsoft", labels);
        Assert.Contains("Windows", labels);
        Assert.Contains("ZzzSentinelFolder", labels);
        Assert.Contains("ZzzSentinelLeaf", labels);
        Assert.DoesNotContain(labels, label => label.StartsWith("[[", StringComparison.Ordinal));
    }

    [Fact]
    public void TopLevelBarsAndAria_AreDrivenByTheLocalizer()
    {
        var cut = Render<MenuBar>();

        var labels = cut.FindAll("button.menu-bar-item").Select(button => button.TextContent.Trim()).ToList();

        Assert.Equal(
            new[] { "[[Menu_File]]", "[[Menu_Edit]]", "[[Menu_View]]", "[[Menu_Tools]]", "[[Menu_Help]]" },
            labels);
        Assert.Equal("[[Menu_MainMenuAria]]", cut.Find("nav.menu-bar").GetAttribute("aria-label"));
    }

    private static void CollectLabels(IReadOnlyList<MenuItem> items, List<string> labels)
    {
        foreach (var item in items)
        {
            if (item.IsSeparator) { continue; }

            labels.Add(item.Label);

            if (item.Children is { } children) { CollectLabels(children, labels); }
        }
    }

    private static ImmutableArray<ChannelReadiness> ReadinessFor(IEnumerable<string> channels) =>
    [
        .. channels.Select(channel => new ChannelReadiness(channel, ChannelPresence.Present, ChannelEnablement.Unknown))
    ];

    private async Task<IReadOnlyList<MenuItem>> OpenMenu(string barLabel)
    {
        IReadOnlyList<MenuItem>? items = null;
        _menuService
            .When(menu => menu.OpenAt(
                Arg.Any<double>(), Arg.Any<double>(), Arg.Any<IReadOnlyList<MenuItem>>(),
                Arg.Any<bool>(), Arg.Any<bool>()))
            .Do(call => items = call.Arg<IReadOnlyList<MenuItem>>());

        var cut = Render<MenuBar>();
        await cut.FindAll("button.menu-bar-item")
            .Single(button => button.TextContent.Trim() == barLabel)
            .ClickAsync(new MouseEventArgs());

        Assert.NotNull(items);

        return items!;
    }
}
