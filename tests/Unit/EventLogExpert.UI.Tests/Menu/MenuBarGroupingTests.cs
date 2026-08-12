// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Common.Versioning;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Scenarios;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.Menu;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.Menu;

public sealed class MenuBarGroupingTests : BunitContext
{
    private const string DisabledReason = "Group events first (column header > Group By)";

    private readonly IMenuActionService _actions = Substitute.For<IMenuActionService>();
    private readonly IAlertDialogService _alertDialogService = Substitute.For<IAlertDialogService>();
    private readonly IEventLogQueries _eventLogQueries = Substitute.For<IEventLogQueries>();
    private readonly IFilterPaneQueries _filterPaneQueries = Substitute.For<IFilterPaneQueries>();
    private readonly IHistogramVisibilitySource _histogramVisibility = Substitute.For<IHistogramVisibilitySource>();
    private readonly ILogTableQueries _logTableQueries = Substitute.For<ILogTableQueries>();
    private readonly IMenuService _menuService = Substitute.For<IMenuService>();
    private readonly IChannelReadinessService _readinessService = Substitute.For<IChannelReadinessService>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly ICurrentVersionProvider _versionProvider = Substitute.For<ICurrentVersionProvider>();

    public MenuBarGroupingTests()
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
        Services.AddSingleton(_versionProvider);

        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/EventLogExpert.UI/Menu/MenuAnchor.js")
            .Setup<MenuAnchorRect>("getMenuElementRect", _ => true)
            .SetResult(new MenuAnchorRect(0, 0, 0, 0, 0, 0));
        _readinessService.GetReadinessAsync(Arg.Any<CancellationToken>()).Returns([]);
        _readinessService.GetReadinessAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => ReadinessFor(call.Arg<IEnumerable<string>>()!));
    }

    [Fact]
    public async Task File_CloseAll_ConfirmAccepted_InvokesCloseAllLogs()
    {
        _logTableQueries.HasActiveLogs().Returns(true);
        _alertDialogService.ShowAlert("Close all logs", Arg.Any<string>(), "Close all", "Cancel").Returns(true);
        var items = await OpenMenu("File");

        await Item(items, "Close All").OnClickAsync!();

        await _actions.Received(1).CloseAllLogsAsync();
    }

    [Fact]
    public async Task File_CloseAll_ConfirmCancelled_DoesNotInvokeCloseAllLogs()
    {
        _logTableQueries.HasActiveLogs().Returns(true);
        _alertDialogService.ShowAlert("Close all logs", Arg.Any<string>(), "Close all", "Cancel").Returns(false);
        var items = await OpenMenu("File");

        await Item(items, "Close All").OnClickAsync!();

        await _actions.DidNotReceive().CloseAllLogsAsync();
    }

    [Fact]
    public async Task File_LiveSecurity_WhenRequiresElevation_RendersStatusAndStaysClickable()
    {
        SetReadiness(
            new ChannelReadiness(LogChannelNames.SecurityLog, ChannelPresence.Present, ChannelEnablement.Enabled)
            {
                Access = ChannelAccess.RequiresElevation
            });

        var live = LiveItems(await OpenMenu("File"));
        var security = Item(live, LogChannelNames.SecurityLog);

        Assert.True(security.IsEnabled);
        Assert.Equal("(elevate)", security.StatusText);
    }

    [Fact]
    public async Task File_OtherLogs_WhenChannelDisabled_RendersStatusAndStaysClickable()
    {
        const string channel = "Microsoft-Windows-Test/Operational";
        SetReadiness(new ChannelReadiness(channel, ChannelPresence.Present, ChannelEnablement.Disabled));
        var live = LiveItems(await OpenMenu("File"));

        var otherLogs = Item(live, "Other Logs");
        var children = await otherLogs.ChildrenLoader!();
        var testFolder = Item(Item(Item(children, "Microsoft").Children!, "Windows").Children!, "Test");
        var operational = Item(testFolder.Children!, "Operational");

        Assert.True(operational.IsEnabled);
        Assert.Equal("(disabled)", operational.StatusText);

        await operational.OnClickAsync!();

        await _actions.Received(1).OpenLiveLogAsync(channel, false);
    }

    [Fact]
    public async Task File_OtherLogs_WhenStateRequiresElevation_RendersStatusAndStaysClickable()
    {
        SetReadiness(
            new ChannelReadiness(LogChannelNames.StateLog, ChannelPresence.Present, ChannelEnablement.Disabled)
            {
                Access = ChannelAccess.RequiresElevation
            });
        var live = LiveItems(await OpenMenu("File"));

        var state = Item(await Item(live, "Other Logs").ChildrenLoader!(), LogChannelNames.StateLog);

        Assert.True(state.IsEnabled);
        Assert.Equal("(elevate)", state.StatusText);
    }

    [Fact]
    public async Task File_WhenActiveLogs_CloseCombineExportEnabled()
    {
        _logTableQueries.HasActiveLogs().Returns(true);

        var items = await OpenMenu("File");

        Assert.True(Item(items, "Close All").IsEnabled);
        Assert.True(Item(items, "Combine").IsEnabled);
        Assert.True(Item(items, "Export to CSV").IsEnabled);
        Assert.True(Item(items, "Export to JSON").IsEnabled);
    }

    [Fact]
    public async Task File_WhenNoActiveLogs_CloseCombineExportDisabled()
    {
        _logTableQueries.HasActiveLogs().Returns(false);

        var items = await OpenMenu("File");

        Assert.False(Item(items, "Close All").IsEnabled);
        Assert.False(Item(items, "Combine").IsEnabled);
        Assert.False(Item(items, "Export to CSV").IsEnabled);
        Assert.False(Item(items, "Export to JSON").IsEnabled);
    }

    [Fact]
    public void Render_DoesNotRequireFluxorServices()
    {
        var cut = Render<MenuBar>();

        Assert.NotEmpty(cut.FindAll("button.menu-bar-item"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task View_ContinuouslyUpdate_CheckReflectsTheQuery(bool continuouslyUpdating)
    {
        _eventLogQueries.IsContinuouslyUpdating().Returns(continuouslyUpdating);

        var item = Item(await OpenViewMenu(), "Continuously Update");

        Assert.Equal(continuouslyUpdating, item.IsChecked);
    }

    [Fact]
    public async Task View_ReadsQueriesAtEachOpen_NotCachedAtInitialization()
    {
        IReadOnlyList<MenuItem>? items = null;
        _menuService
            .When(menu => menu.OpenAt(
                Arg.Any<double>(), Arg.Any<double>(), Arg.Any<IReadOnlyList<MenuItem>>(),
                Arg.Any<bool>(), Arg.Any<bool>()))
            .Do(call => items = call.Arg<IReadOnlyList<MenuItem>>());
        _menuService.ActiveItems.Returns((IReadOnlyList<MenuItem>?)null);
        _eventLogQueries.IsContinuouslyUpdating().Returns(false);

        var cut = Render<MenuBar>();

        await cut.FindAll("button.menu-bar-item").Single(button => button.TextContent.Trim() == "View")
            .ClickAsync(new MouseEventArgs());
        Assert.False(Item(items!, "Continuously Update").IsChecked);

        _eventLogQueries.IsContinuouslyUpdating().Returns(true);
        await cut.FindAll("button.menu-bar-item").Single(button => button.TextContent.Trim() == "View")
            .ClickAsync(new MouseEventArgs());

        Assert.True(Item(items!, "Continuously Update").IsChecked);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task View_ShowAllEvents_CheckedWhenFilterPaneDisabled(bool filterPaneEnabled, bool expectedChecked)
    {
        _filterPaneQueries.IsEnabled().Returns(filterPaneEnabled);

        var item = Item(await OpenViewMenu(), "Show All Events");

        Assert.True(item.IsEnabled);
        Assert.Equal(expectedChecked, item.IsChecked);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task View_Timeline_CheckReflectsHistogramVisibility(bool visible)
    {
        _histogramVisibility.IsVisible.Returns(visible);

        var item = Item(await OpenViewMenu(), "Timeline");

        Assert.Equal(visible, item.IsChecked);
    }

    [Fact]
    public async Task View_WhenGroupingAscending_DescendingEnabledButUnchecked()
    {
        _logTableQueries.IsGrouping().Returns(true);
        _logTableQueries.IsGroupDescending().Returns(false);

        var descending = Item(await OpenViewMenu(), "Group Descending");

        Assert.True(descending.IsEnabled);
        Assert.False(descending.IsChecked);
    }

    [Fact]
    public async Task View_WhenGroupingDescending_GroupActionsEnabledAndDescendingChecked()
    {
        _logTableQueries.IsGrouping().Returns(true);
        _logTableQueries.IsGroupDescending().Returns(true);

        var items = await OpenViewMenu();

        Assert.True(Item(items, "Expand All Groups").IsEnabled);
        Assert.True(Item(items, "Collapse All Groups").IsEnabled);

        var descending = Item(items, "Group Descending");
        Assert.True(descending.IsEnabled);
        Assert.True(descending.IsChecked);
    }

    [Fact]
    public async Task View_WhenNotGrouping_GroupActionsDisabledWithReason()
    {
        _logTableQueries.IsGrouping().Returns(false);

        var items = await OpenViewMenu();

        foreach (var label in new[] { "Expand All Groups", "Collapse All Groups", "Group Descending" })
        {
            var item = Item(items, label);
            Assert.False(item.IsEnabled);
            Assert.Equal(DisabledReason, item.DisabledReason);
        }
    }

    private static MenuItem Item(IReadOnlyList<MenuItem> items, string label) =>
        items.Single(item => item.Label == label);

    private static IReadOnlyList<MenuItem> LiveItems(IReadOnlyList<MenuItem> fileItems) =>
        Item(Item(fileItems, "Open").Children!, "Live").Children!;

    private static ImmutableArray<ChannelReadiness> ReadinessFor(IEnumerable<string> channels) =>
    [
        .. channels.Select(channel => new ChannelReadiness(channel, ChannelPresence.Present, ChannelEnablement.Unknown))
    ];

    private async Task<IReadOnlyList<MenuItem>> OpenMenu(string barLabel)
    {
        IReadOnlyList<MenuItem>? items = null;
        _menuService
            .When(m => m.OpenAt(
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

    private Task<IReadOnlyList<MenuItem>> OpenViewMenu() => OpenMenu("View");

    private void SetReadiness(params ChannelReadiness[] readiness)
    {
        _readinessService.GetReadinessAsync(Arg.Any<CancellationToken>()).Returns([.. readiness]);
        _readinessService.GetReadinessAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var requested = call.Arg<IEnumerable<string>>()!.ToHashSet(StringComparer.OrdinalIgnoreCase);

                return readiness
                    .Where(channel => requested.Contains(channel.Channel))
                    .ToImmutableArray();
            });
    }
}
