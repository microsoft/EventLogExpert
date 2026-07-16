// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Common.Versioning;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.Menu;
using Fluxor;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Menu;

public sealed class MenuBarGroupingTests : BunitContext
{
    private const string DisabledReason = "Group events first (column header > Group By)";

    private readonly IMenuActionService _actions = Substitute.For<IMenuActionService>();
    private readonly IAlertDialogService _alertDialogService = Substitute.For<IAlertDialogService>();
    private readonly IStateSelection<EventLogState, bool> _eventLogSelection = Substitute.For<IStateSelection<EventLogState, bool>>();
    private readonly IStateSelection<FilterPaneState, bool> _filterPaneIsEnabled = Substitute.For<IStateSelection<FilterPaneState, bool>>();
    private readonly IStateSelection<HistogramState, bool> _histogramVisible = Substitute.For<IStateSelection<HistogramState, bool>>();
    private readonly List<IStateSelection<LogTableState, bool>> _logTableSelections = [];
    private readonly IMenuService _menuService = Substitute.For<IMenuService>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly ICurrentVersionProvider _versionProvider = Substitute.For<ICurrentVersionProvider>();

    private LogTableState _logTableState = new();

    public MenuBarGroupingTests()
    {
        Services.AddSingleton(_actions);
        Services.AddSingleton(_alertDialogService);
        Services.AddSingleton(_eventLogSelection);
        Services.AddSingleton(_filterPaneIsEnabled);
        Services.AddSingleton(_histogramVisible);
        Services.AddTransient<IStateSelection<LogTableState, bool>>(_ => CreateLogTableSelection());
        Services.AddSingleton(_menuService);
        Services.AddSingleton(_settings);
        Services.AddSingleton(_versionProvider);

        Services.AddFluxor(options => options.ScanAssemblies(typeof(MenuBar).Assembly));

        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/EventLogExpert.UI/Menu/MenuAnchor.js")
            .Setup<MenuAnchorRect>("getMenuElementRect", _ => true)
            .SetResult(new MenuAnchorRect(0, 0, 0, 0, 0, 0));
    }

    [Fact]
    public async Task File_CloseAll_ConfirmAccepted_InvokesCloseAllLogs()
    {
        _logTableState = new LogTableState
        {
            EventTables = [new LogView(new EventLogId(Guid.NewGuid())) { LogName = "Application" }],
        };
        _alertDialogService.ShowAlert("Close all logs", Arg.Any<string>(), "Close all", "Cancel").Returns(true);
        var items = await OpenMenu("File");

        await Item(items, "Close All").OnClickAsync!();

        await _actions.Received(1).CloseAllLogsAsync();
    }

    [Fact]
    public async Task File_CloseAll_ConfirmCancelled_DoesNotInvokeCloseAllLogs()
    {
        _logTableState = new LogTableState
        {
            EventTables = [new LogView(new EventLogId(Guid.NewGuid())) { LogName = "Application" }],
        };
        _alertDialogService.ShowAlert("Close all logs", Arg.Any<string>(), "Close all", "Cancel").Returns(false);
        var items = await OpenMenu("File");

        await Item(items, "Close All").OnClickAsync!();

        await _actions.DidNotReceive().CloseAllLogsAsync();
    }

    [Fact]
    public async Task File_WhenActiveLogOpen_CloseAllAndCombineEnabled()
    {
        _logTableState = new LogTableState
        {
            EventTables = [new LogView(new EventLogId(Guid.NewGuid())) { LogName = "Application" }],
        };

        var items = await OpenMenu("File");

        Assert.True(Item(items, "Close All").IsEnabled);
        Assert.True(Item(items, "Combine").IsEnabled);
    }

    [Fact]
    public async Task File_WhenMultipleLogsWithCombinedView_CloseAllAndCombineEnabled()
    {
        _logTableState = new LogTableState
        {
            EventTables =
            [
                new LogView(new EventLogId(Guid.NewGuid())) { GroupId = LogTabGroupId.AllLogs },
                new LogView(new EventLogId(Guid.NewGuid())) { LogName = "Application" },
                new LogView(new EventLogId(Guid.NewGuid())) { LogName = "System" },
            ],
        };

        var items = await OpenMenu("File");

        Assert.True(Item(items, "Close All").IsEnabled);
        Assert.True(Item(items, "Combine").IsEnabled);
    }

    [Fact]
    public async Task File_WhenNoLogsOpen_CloseAllAndCombineDisabled()
    {
        _logTableState = new LogTableState();

        var items = await OpenMenu("File");

        Assert.False(Item(items, "Close All").IsEnabled);
        Assert.False(Item(items, "Combine").IsEnabled);
    }

    [Fact]
    public async Task File_WhenOnlyCombinedView_CloseAllAndCombineDisabled()
    {
        _logTableState = new LogTableState
        {
            EventTables = [new LogView(new EventLogId(Guid.NewGuid())) { GroupId = LogTabGroupId.AllLogs }],
        };

        var items = await OpenMenu("File");

        Assert.False(Item(items, "Close All").IsEnabled);
        Assert.False(Item(items, "Combine").IsEnabled);
    }

    [Fact]
    public void Render_SubscribesAllLogTableSelections()
    {
        Render<MenuBar>();

        Assert.Equal(3, _logTableSelections.Count);
        Assert.All(_logTableSelections, s => s.Received(1).Select(Arg.Any<Func<LogTableState, bool>>()));
    }

    [Fact]
    public async Task View_WhenGroupingAscending_DescendingEnabledButUnchecked()
    {
        _logTableState = new LogTableState { GroupBy = ColumnName.Source, IsGroupDescending = false };

        var descending = Item(await OpenViewMenu(), "Group Descending");

        Assert.True(descending.IsEnabled);
        Assert.False(descending.IsChecked);
    }

    [Fact]
    public async Task View_WhenGroupingDescending_GroupActionsEnabledAndDescendingChecked()
    {
        _logTableState = new LogTableState { GroupBy = ColumnName.Source, IsGroupDescending = true };

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
        _logTableState = new LogTableState { GroupBy = null };

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

    // Distinct substitute per selection; Value applies its own projection to _logTableState.
    private IStateSelection<LogTableState, bool> CreateLogTableSelection()
    {
        var selection = Substitute.For<IStateSelection<LogTableState, bool>>();
        Func<LogTableState, bool>? selector = null;
        selection.When(s => s.Select(Arg.Any<Func<LogTableState, bool>>()))
            .Do(call => selector = call.Arg<Func<LogTableState, bool>>());
        selection.Value.Returns(_ => selector is not null && selector(_logTableState));
        _logTableSelections.Add(selection);

        return selection;
    }

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
}
