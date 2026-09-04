// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Localization;
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
using Microsoft.Extensions.Localization;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.Menu;

public sealed class MenuBarGroupingTests : BunitContext
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
        Services.AddEventLogLocalization();

        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/EventLogExpert.UI/Menu/MenuAnchor.js")
            .Setup<MenuAnchorRect>("getMenuElementRect", _ => true)
            .SetResult(new MenuAnchorRect(0, 0, 0, 0, 0, 0));
        _readinessService.GetReadinessAsync(Arg.Any<CancellationToken>()).Returns([]);
        _readinessService.GetReadinessAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => ReadinessFor(call.Arg<IEnumerable<string>>()!));
    }

    private IStringLocalizer<SharedResource> Localizer =>
        Services.GetRequiredService<IStringLocalizer<SharedResource>>();

    [Fact]
    public async Task File_CloseAll_ConfirmAccepted_InvokesCloseAllLogs()
    {
        _logTableQueries.HasActiveLogs().Returns(true);
        _alertDialogService.ShowAlert(Localizer["CloseAllLogs_Title"].Value, Arg.Any<string>(), Localizer["CloseAllLogs_Confirm"].Value, Localizer["Modal_Cancel"].Value).Returns(true);
        var items = await OpenMenu(Localizer["Menu_File"].Value);

        await Item(items, Localizer["Menu_File_CloseAll"].Value).OnClickAsync!();

        await _actions.Received(1).CloseAllLogsAsync();
    }

    [Fact]
    public async Task File_CloseAll_ConfirmCancelled_DoesNotInvokeCloseAllLogs()
    {
        _logTableQueries.HasActiveLogs().Returns(true);
        _alertDialogService.ShowAlert(Localizer["CloseAllLogs_Title"].Value, Arg.Any<string>(), Localizer["CloseAllLogs_Confirm"].Value, Localizer["Modal_Cancel"].Value).Returns(false);
        var items = await OpenMenu(Localizer["Menu_File"].Value);

        await Item(items, Localizer["Menu_File_CloseAll"].Value).OnClickAsync!();

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

        var live = LiveItems(await OpenMenu(Localizer["Menu_File"].Value));
        var security = Item(live, LogChannelNames.SecurityLog);

        Assert.True(security.IsEnabled);
        Assert.Equal(Localizer["Menu_Status_Elevate"].Value, security.StatusText);
    }

    [Fact]
    public async Task File_OtherLogs_WhenChannelDisabled_RendersStatusAndStaysClickable()
    {
        const string channel = "Microsoft-Windows-Test/Operational";
        SetReadiness(new ChannelReadiness(channel, ChannelPresence.Present, ChannelEnablement.Disabled));
        var live = LiveItems(await OpenMenu(Localizer["Menu_File"].Value));

        var otherLogs = Item(live, Localizer["Menu_Open_OtherLogs"].Value);
        var children = await otherLogs.ChildrenLoader!();
        var testFolder = Item(Item(Item(children, "Microsoft").Children!, "Windows").Children!, "Test");
        var operational = Item(testFolder.Children!, "Operational");

        Assert.True(operational.IsEnabled);
        Assert.Equal(Localizer["Menu_Status_Disabled"].Value, operational.StatusText);

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
        var live = LiveItems(await OpenMenu(Localizer["Menu_File"].Value));

        var state = Item(await Item(live, Localizer["Menu_Open_OtherLogs"].Value).ChildrenLoader!(), LogChannelNames.StateLog);

        Assert.True(state.IsEnabled);
        Assert.Equal(Localizer["Menu_Status_Elevate"].Value, state.StatusText);
    }

    [Fact]
    public async Task File_WhenActiveLogs_CloseCombineExportEnabled()
    {
        _logTableQueries.HasActiveLogs().Returns(true);

        var items = await OpenMenu(Localizer["Menu_File"].Value);

        Assert.True(Item(items, Localizer["Menu_File_CloseAll"].Value).IsEnabled);
        Assert.True(Item(items, Localizer["Menu_File_Combine"].Value).IsEnabled);
        Assert.True(Item(items, Localizer["Menu_File_ExportCsv"].Value).IsEnabled);
        Assert.True(Item(items, Localizer["Menu_File_ExportJson"].Value).IsEnabled);
    }

    [Fact]
    public async Task File_WhenNoActiveLogs_CloseCombineExportDisabled()
    {
        _logTableQueries.HasActiveLogs().Returns(false);

        var items = await OpenMenu(Localizer["Menu_File"].Value);

        Assert.False(Item(items, Localizer["Menu_File_CloseAll"].Value).IsEnabled);
        Assert.False(Item(items, Localizer["Menu_File_Combine"].Value).IsEnabled);
        Assert.False(Item(items, Localizer["Menu_File_ExportCsv"].Value).IsEnabled);
        Assert.False(Item(items, Localizer["Menu_File_ExportJson"].Value).IsEnabled);
    }

    [Fact]
    public async Task MenuBarItem_KeyboardActivation_OpensMenuWithKeyboardFocusFlag()
    {
        var cut = Render<MenuBar>();

        await cut.FindAll("button.menu-bar-item")
            .Single(button => button.TextContent.Trim() == Localizer["Menu_File"].Value)
            .ClickAsync(new MouseEventArgs { Detail = 0 });

        _menuService.Received(1).OpenAt(
            Arg.Any<double>(), Arg.Any<double>(), Arg.Any<IReadOnlyList<MenuItem>>(),
            Arg.Any<bool>(), Arg.Any<bool>(), true);
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

        var item = Item(await OpenViewMenu(), Localizer["Menu_View_ContinuouslyUpdate"].Value);

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

        await cut.FindAll("button.menu-bar-item").Single(button => button.TextContent.Trim() == Localizer["Menu_View"].Value)
            .ClickAsync(new MouseEventArgs { Detail = 1 });
        Assert.False(Item(items!, Localizer["Menu_View_ContinuouslyUpdate"].Value).IsChecked);

        _eventLogQueries.IsContinuouslyUpdating().Returns(true);
        await cut.FindAll("button.menu-bar-item").Single(button => button.TextContent.Trim() == Localizer["Menu_View"].Value)
            .ClickAsync(new MouseEventArgs { Detail = 1 });

        Assert.True(Item(items!, Localizer["Menu_View_ContinuouslyUpdate"].Value).IsChecked);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task View_ShowAllEvents_CheckedWhenFilterPaneDisabled(bool filterPaneEnabled, bool expectedChecked)
    {
        _filterPaneQueries.IsEnabled().Returns(filterPaneEnabled);

        var item = Item(await OpenViewMenu(), Localizer["Menu_View_ShowAllEvents"].Value);

        Assert.True(item.IsEnabled);
        Assert.Equal(expectedChecked, item.IsChecked);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task View_Timeline_CheckReflectsHistogramVisibility(bool visible)
    {
        _histogramVisibility.IsVisible.Returns(visible);

        var item = Item(await OpenViewMenu(), Localizer["Menu_View_Timeline"].Value);

        Assert.Equal(visible, item.IsChecked);
    }

    [Fact]
    public async Task View_WhenGroupingAscending_DescendingEnabledButUnchecked()
    {
        _logTableQueries.IsGrouping().Returns(true);
        _logTableQueries.IsGroupDescending().Returns(false);

        var descending = Item(await OpenViewMenu(), Localizer["Menu_View_GroupDescending"].Value);

        Assert.True(descending.IsEnabled);
        Assert.False(descending.IsChecked);
    }

    [Fact]
    public async Task View_WhenGroupingDescending_GroupActionsEnabledAndDescendingChecked()
    {
        _logTableQueries.IsGrouping().Returns(true);
        _logTableQueries.IsGroupDescending().Returns(true);

        var items = await OpenViewMenu();

        Assert.True(Item(items, Localizer["Menu_View_ExpandAllGroups"].Value).IsEnabled);
        Assert.True(Item(items, Localizer["Menu_View_CollapseAllGroups"].Value).IsEnabled);

        var descending = Item(items, Localizer["Menu_View_GroupDescending"].Value);
        Assert.True(descending.IsEnabled);
        Assert.True(descending.IsChecked);
    }

    [Fact]
    public async Task View_WhenNotGrouping_GroupActionsDisabledWithReason()
    {
        _logTableQueries.IsGrouping().Returns(false);

        var items = await OpenViewMenu();

        foreach (var label in new[] { Localizer["Menu_View_ExpandAllGroups"].Value, Localizer["Menu_View_CollapseAllGroups"].Value, Localizer["Menu_View_GroupDescending"].Value })
        {
            var item = Item(items, label);
            Assert.False(item.IsEnabled);
            Assert.Equal(Localizer["Menu_GroupDisabledReason"].Value, item.DisabledReason);
        }
    }

    private static MenuItem Item(IReadOnlyList<MenuItem> items, string label) =>
        items.Single(item => item.Label == label);

    private static ImmutableArray<ChannelReadiness> ReadinessFor(IEnumerable<string> channels) =>
    [
        .. channels.Select(channel => new ChannelReadiness(channel, ChannelPresence.Present, ChannelEnablement.Unknown))
    ];

    private IReadOnlyList<MenuItem> LiveItems(IReadOnlyList<MenuItem> fileItems) =>
        Item(Item(fileItems, Localizer["Menu_File_Open"].Value).Children!, Localizer["Menu_Open_Live"].Value).Children!;

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
            .ClickAsync(new MouseEventArgs { Detail = 1 });

        Assert.NotNull(items);

        return items!;
    }

    private Task<IReadOnlyList<MenuItem>> OpenViewMenu() => OpenMenu(Localizer["Menu_View"].Value);

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
