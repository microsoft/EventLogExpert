// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Localization;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Scenarios;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.Menu;
using EventLogExpert.UI.Tests.Localization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;
using System.Collections.Immutable;
using System.Xml.Linq;

namespace EventLogExpert.UI.Tests.Menu;

/// <summary>
///     Localization coverage for the Menu bar: top-level + item labels resolve, the tree carries no leaked resource
///     keys (items never reach the DOM in tests, so the guard walks the captured <see cref="MenuItem" /> tree), aria
///     chrome + status badges + disabled reasons are localized, channel names stay verbatim data, and every authored
///     Menu_*/CloseAllLogs_* key is referenced in source (orphan guard).
/// </summary>
public sealed class MenuLocalizationTests : BunitContext
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

    public MenuLocalizationTests()
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

    public static IEnumerable<object[]> AllMenuKeys() =>
    [
        ["Menu_File", "File"], ["Menu_Edit", "Edit"], ["Menu_View", "View"], ["Menu_Tools", "Tools"], ["Menu_Help", "Help"],
        ["Menu_File_Open", "Open"], ["Menu_File_Combine", "Combine"], ["Menu_File_CloseAll", "Close All"],
        ["Menu_File_ExportCsv", "Export to CSV"], ["Menu_File_ExportJson", "Export to JSON"], ["Menu_File_Exit", "Exit"],
        ["Menu_Open_File", "File"], ["Menu_Open_Folder", "Folder"], ["Menu_Open_FolderTopLevel", "Folder (top level only)"],
        ["Menu_Open_Live", "Live"], ["Menu_Open_OtherLogs", "Other Logs"],
        ["Menu_Edit_CopySelected", "Copy Selected"], ["Menu_Edit_CopySelectedSimple", "Copy Selected (Simple)"],
        ["Menu_Edit_CopySelectedXml", "Copy Selected (XML)"], ["Menu_Edit_CopySelectedFull", "Copy Selected (Full)"],
        ["Menu_Edit_CopySelectedMarkdown", "Copy Selected (Markdown)"],
        ["Menu_View_ShowAllEvents", "Show All Events"], ["Menu_View_LoadNewEvents", "Load New Events"],
        ["Menu_View_ContinuouslyUpdate", "Continuously Update"], ["Menu_View_GroupDescending", "Group Descending"],
        ["Menu_View_ExpandAllGroups", "Expand All Groups"], ["Menu_View_CollapseAllGroups", "Collapse All Groups"],
        ["Menu_View_Timeline", "Timeline"], ["Menu_View_ResolutionCoverage", "Resolution & Coverage"],
        ["Menu_Tools_Databases", "Databases..."], ["Menu_Tools_Settings", "Settings"],
        ["Menu_Help_Docs", "Docs"], ["Menu_Help_SubmitIssue", "Submit an Issue"], ["Menu_Help_CheckForUpdates", "Check for Updates"],
        ["Menu_Help_ReleaseNotes", "Release Notes"], ["Menu_Help_ViewLogs", "View Logs"],
        ["Menu_Status_Elevate", "(elevate)"], ["Menu_Status_Disabled", "(disabled)"],
        ["Menu_GroupDisabledReason", "Group events first (column header > Group By)"],
        ["Menu_ResolutionCoverageDisabledReason", "Open a log to view resolution coverage."],
        ["Menu_Shortcut_Copy", "Ctrl+C"], ["Menu_Shortcut_Open", "Ctrl+O"], ["Menu_Shortcut_ShowAll", "Ctrl+H"],
        ["Menu_MainMenuAria", "Main menu"], ["Menu_PopupAria", "Menu"], ["Menu_LoadingAria", "Loading"], ["Menu_Loading", "Loading..."],
        ["CloseAllLogs_Title", "Close all logs"], ["CloseAllLogs_Body", "Close all open logs? This cannot be undone."],
        ["CloseAllLogs_Confirm", "Close all"],
    ];

    [Fact]
    public void AllMenuKeys_TheoryData_CoversEveryMenuResourceKey()
    {
        var resxKeys = XDocument.Load(LocalizationSourceScan.ResxPath)
            .Root!.Elements("data")
            .Select(data => (string?)data.Attribute("name"))
            .Where(name => name is not null &&
                (name.StartsWith("Menu_", StringComparison.Ordinal) ||
                 name.StartsWith("CloseAllLogs_", StringComparison.Ordinal)))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var theoryKeys = AllMenuKeys()
            .Select(row => (string)row[0])
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        // Guards against value-pin coverage rot: a future Menu_*/CloseAllLogs_* key must be added to AllMenuKeys.
        Assert.Equal(resxKeys, theoryKeys);
    }

    [Fact]
    public async Task CloseAllConfirmation_UsesLocalizedStrings()
    {
        _logTableQueries.HasActiveLogs().Returns(true);

        var closeAll = Item(await OpenMenu(Localizer["Menu_File"].Value), Localizer["Menu_File_CloseAll"].Value);
        await closeAll.OnClickAsync!();

        await _alertDialogService.Received(1).ShowAlert(
            Localizer["CloseAllLogs_Title"].Value,
            Localizer["CloseAllLogs_Body"].Value,
            Localizer["CloseAllLogs_Confirm"].Value,
            Localizer["Modal_Cancel"].Value);
    }

    [Fact]
    public async Task CopyShortcutHint_IsLocalized_ByteIdentical()
    {
        _settings.CopyFormat.Returns(EventCopyFormat.Default);

        var editItems = await OpenMenu(Localizer["Menu_Edit"].Value);
        var copyDefault = editItems.Single(item => item.Label == Localizer["Menu_Edit_CopySelected"].Value);

        Assert.Equal(Localizer["Menu_Shortcut_Copy"].Value, copyDefault.Shortcut);
        Assert.Equal("Ctrl+C", copyDefault.Shortcut);
    }

    [Fact]
    public async Task EveryMenuItem_DoesNotLeakResourceKeys()
    {
        foreach (var bar in new[] { "File", "Edit", "View", "Tools", "Help" })
        {
            var items = await OpenMenu(Localizer[$"Menu_{bar}"].Value);
            await AssertNoLeakedKeys(items);
        }
    }

    [Fact]
    public async Task GroupDisabledReason_IsLocalized()
    {
        _logTableQueries.IsGrouping().Returns(false);

        var item = Item(await OpenMenu(Localizer["Menu_View"].Value), Localizer["Menu_View_GroupDescending"].Value);

        Assert.False(item.IsEnabled);
        Assert.Equal(Localizer["Menu_GroupDisabledReason"].Value, item.DisabledReason);
    }

    [Fact]
    public async Task LiveChannelNames_RenderVerbatim_NotLocalized()
    {
        var liveItems = await OpenLiveSubmenu();
        var labels = liveItems.Where(item => !item.IsSeparator && item.ChildrenLoader is null)
            .Select(item => item.Label)
            .ToList();

        // Channel names are Windows proper nouns passed through as data, never routed through the localizer.
        Assert.Equal(
            new[] { LogChannelNames.ApplicationLog, LogChannelNames.SystemLog, LogChannelNames.SecurityLog },
            labels);
    }

    [Theory]
    [InlineData(ChannelAccess.RequiresElevation, ChannelEnablement.Enabled, "Menu_Status_Elevate")]
    [InlineData(ChannelAccess.Accessible, ChannelEnablement.Disabled, "Menu_Status_Disabled")]
    public async Task LiveChannelStatusBadges_AreLocalized(ChannelAccess access, ChannelEnablement enablement, string key)
    {
        _readinessService.GetReadinessAsync(Arg.Any<CancellationToken>())
            .Returns(
            [
                new ChannelReadiness(LogChannelNames.ApplicationLog, ChannelPresence.Present, enablement)
                {
                    Access = access
                }
            ]);

        var liveItems = await OpenLiveSubmenu();
        var application = liveItems.Single(item => item.Label == LogChannelNames.ApplicationLog);

        Assert.Equal(Localizer[key].Value, application.StatusText);
    }

    [Fact]
    public void MainMenuAria_IsLocalized()
    {
        var cut = Render<MenuBar>();

        Assert.Equal(Localizer["Menu_MainMenuAria"].Value, cut.Find("nav.menu-bar").GetAttribute("aria-label"));
    }

    [Fact]
    public async Task OtherLogsTree_FolderAndLeafNames_RenderVerbatim_NotLocalized()
    {
        const string sentinelChannel = "Microsoft-Windows-ZzzSentinelFolder/ZzzSentinelLeaf";
        _readinessService.GetReadinessAsync(Arg.Any<CancellationToken>())
            .Returns([new ChannelReadiness(sentinelChannel, ChannelPresence.Present, ChannelEnablement.Unknown)]);

        var liveItems = await OpenLiveSubmenu();
        var otherLogs = liveItems.Single(item => item.ChildrenLoader is not null);
        var tree = await otherLogs.ChildrenLoader!();

        var labels = new List<string>();
        CollectLabels(tree, labels);

        // GetMenuPath("Microsoft-Windows-ZzzSentinelFolder/ZzzSentinelLeaf") -> Microsoft > Windows > ZzzSentinelFolder > ZzzSentinelLeaf.
        // Every folder + leaf segment is system data rendered verbatim, never routed through the localizer.
        Assert.Contains("Microsoft", labels);
        Assert.Contains("Windows", labels);
        Assert.Contains("ZzzSentinelFolder", labels);
        Assert.Contains("ZzzSentinelLeaf", labels);
        Assert.DoesNotContain(labels, label => label.Contains("Menu_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RenderedMenuItems_DoNotLeakResourceKeysToTheDom()
    {
        foreach (var bar in new[] { "File", "Edit", "View", "Tools", "Help" })
        {
            var items = await OpenMenu(Localizer[$"Menu_{bar}"].Value);
            var rendered = Render<MenuRenderer>(parameters => parameters.Add(panel => panel.Items, items));

            Assert.DoesNotContain("Menu_", rendered.Markup);
            Assert.DoesNotContain("CloseAllLogs_", rendered.Markup);
        }
    }

    [Fact]
    public async Task RepresentativeItemLabels_ResolveToLocalizedValues()
    {
        var fileItems = await OpenMenu(Localizer["Menu_File"].Value);
        Assert.Contains(fileItems, item => item.Label == Localizer["Menu_File_ExportCsv"].Value);
        Assert.Contains(fileItems, item => item.Label == Localizer["Menu_File_Exit"].Value);

        var helpItems = await OpenMenu(Localizer["Menu_Help"].Value);
        Assert.Contains(helpItems, item => item.Label == Localizer["Menu_Help_SubmitIssue"].Value);

        var viewItems = await OpenMenu(Localizer["Menu_View"].Value);
        Assert.Contains(viewItems, item => item.Label == Localizer["Menu_View_ResolutionCoverage"].Value);
    }

    [Fact]
    public async Task ResolutionCoverageDisabledReason_IsLocalized()
    {
        _logTableQueries.HasActiveLogs().Returns(false);

        var item = Item(await OpenMenu(Localizer["Menu_View"].Value), Localizer["Menu_View_ResolutionCoverage"].Value);

        Assert.False(item.IsEnabled);
        Assert.Equal(Localizer["Menu_ResolutionCoverageDisabledReason"].Value, item.DisabledReason);
    }

    [Theory]
    [MemberData(nameof(AllMenuKeys))]
    public void ResourceValue_IsByteIdenticalEnglish(string key, string expected) =>
        Assert.Equal(expected, Localizer[key].Value);

    [Fact]
    public void TopLevelBars_AreLocalized()
    {
        var cut = Render<MenuBar>();

        var labels = cut.FindAll("button.menu-bar-item").Select(button => button.TextContent.Trim()).ToList();

        Assert.Equal(
            new[]
            {
                Localizer["Menu_File"].Value,
                Localizer["Menu_Edit"].Value,
                Localizer["Menu_View"].Value,
                Localizer["Menu_Tools"].Value,
                Localizer["Menu_Help"].Value,
            },
            labels);
        Assert.DoesNotContain("Menu_", cut.Markup);
    }

    private static void AssertFieldsClean(string? value)
    {
        if (value is null) { return; }

        Assert.DoesNotContain("Menu_", value);
        Assert.DoesNotContain("CloseAllLogs_", value);
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

    private static MenuItem Item(IReadOnlyList<MenuItem> items, string label) =>
        items.Single(item => item.Label == label);

    private static ImmutableArray<ChannelReadiness> ReadinessFor(IEnumerable<string> channels) =>
    [
        .. channels.Select(channel => new ChannelReadiness(channel, ChannelPresence.Present, ChannelEnablement.Unknown))
    ];

    private async Task AssertNoLeakedKeys(IReadOnlyList<MenuItem> items)
    {
        foreach (var item in items)
        {
            AssertFieldsClean(item.Label);
            AssertFieldsClean(item.StatusText);
            AssertFieldsClean(item.DisabledReason);
            AssertFieldsClean(item.Shortcut);

            if (item.Children is { } children) { await AssertNoLeakedKeys(children); }

            if (item.ChildrenLoader is { } loader) { await AssertNoLeakedKeys(await loader()); }
        }
    }

    private async Task<IReadOnlyList<MenuItem>> OpenLiveSubmenu()
    {
        var fileItems = await OpenMenu(Localizer["Menu_File"].Value);
        var openItems = Item(fileItems, Localizer["Menu_File_Open"].Value).Children!;

        return Item(openItems, Localizer["Menu_Open_Live"].Value).Children!;
    }

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
