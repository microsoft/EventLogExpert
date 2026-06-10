// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Runtime.Common.Versioning;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterPane;
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
    private readonly IStateSelection<EventLogState, bool> _eventLogSelection = Substitute.For<IStateSelection<EventLogState, bool>>();
    private readonly IStateSelection<FilterPaneState, bool> _filterPaneIsEnabled = Substitute.For<IStateSelection<FilterPaneState, bool>>();
    private readonly IStateSelection<LogTableState, bool> _groupingSelection = Substitute.For<IStateSelection<LogTableState, bool>>();
    private readonly IMenuService _menuService = Substitute.For<IMenuService>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly ICurrentVersionProvider _versionProvider = Substitute.For<ICurrentVersionProvider>();

    public MenuBarGroupingTests()
    {
        Services.AddSingleton(_actions);
        Services.AddSingleton(_eventLogSelection);
        Services.AddSingleton(_filterPaneIsEnabled);
        Services.AddSingleton(_groupingSelection);
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
    public void Render_SubscribesBothGroupingSelections()
    {
        Render<MenuBar>();

        _groupingSelection.Received(2).Select(Arg.Any<Func<LogTableState, bool>>());
    }

    [Fact]
    public async Task View_WhenGrouping_GroupActionsEnabledAndDescendingChecked()
    {
        _groupingSelection.Value.Returns(true);

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
        _groupingSelection.Value.Returns(false);

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

    private async Task<IReadOnlyList<MenuItem>> OpenViewMenu()
    {
        IReadOnlyList<MenuItem>? items = null;
        _menuService
            .When(m => m.OpenAt(
                Arg.Any<double>(), Arg.Any<double>(), Arg.Any<IReadOnlyList<MenuItem>>(),
                Arg.Any<bool>(), Arg.Any<bool>()))
            .Do(call => items = call.Arg<IReadOnlyList<MenuItem>>());

        var cut = Render<MenuBar>();
        await cut.FindAll("button.menu-bar-item")
            .Single(button => button.TextContent.Trim() == "View")
            .ClickAsync(new MouseEventArgs());

        Assert.NotNull(items);

        return items!;
    }
}
