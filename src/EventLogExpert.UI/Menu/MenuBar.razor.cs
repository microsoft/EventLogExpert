// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.Export;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Scenarios;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.Common.Interop;
using EventLogExpert.UI.Inputs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using System.Text;

namespace EventLogExpert.UI.Menu;

public sealed partial class MenuBar
{
    private readonly List<TopLevel> _bars = [];

    private ChromelessButton?[] _barElements = [];
    private int _focusedBarIndex;
    private IJSObjectReference? _menuAnchorModule;
    private ElementReference _menuBarRootRef;
    private long _openRequestId;
    private IReadOnlyDictionary<string, ChannelReadiness> _readinessByChannel =
        new Dictionary<string, ChannelReadiness>(StringComparer.OrdinalIgnoreCase);
    private IJSObjectReference? _scrollSuppressorModule;

    [Inject] private IMenuActionService Actions { get; init; } = null!;

    private TopLevel? ActiveBar { get; set; }

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IChannelReadinessService ChannelReadinessService { get; init; } = null!;

    [Inject] private IEventLogQueries EventLogQueries { get; init; } = null!;

    [Inject] private IFilterPaneQueries FilterPaneQueries { get; init; } = null!;

    private string GroupDisabledReason => Localizer["Menu_GroupDisabledReason"];

    [Inject] private IHistogramVisibilitySource HistogramVisibility { get; init; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private IStringLocalizer<SharedResource> Localizer { get; init; } = null!;

    [Inject] private ILogTableQueries LogTableQueries { get; init; } = null!;

    [Inject] private IMenuService MenuService { get; init; } = null!;

    [Inject] private ISettingsService Settings { get; init; } = null!;

    protected override async ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing)
        {
            InvalidatePendingOpen();

            Settings.CopyFormatChanged -= OnSettingsChanged;
            MenuService.StateChanged -= OnMenuServiceStateChanged;
            MenuService.NavigateBarRequested -= OnNavigateBarRequested;

            await JsModuleInterop.DisposeModuleSafelyAsync(
                _scrollSuppressorModule,
                module => module.InvokeVoidAsync("release", _menuBarRootRef));

            await JsModuleInterop.DisposeModuleSafelyAsync(_menuAnchorModule);

            _menuAnchorModule = null;
            _scrollSuppressorModule = null;
        }

        await base.DisposeAsyncCore(disposing);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                _scrollSuppressorModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
                    "import",
                    "./_content/EventLogExpert.UI/Common/keyboardScrollSuppressor.js");

                await _scrollSuppressorModule.InvokeVoidAsync(
                    "suppress",
                    _menuBarRootRef,
                    new[]
                    {
                        new
                        {
                            selector = "[role='menuitem']",
                            keys = new[] { "ArrowRight", "ArrowLeft", "Home", "End", "ArrowDown", "ArrowUp" }
                        }
                    });
            }
            catch (JSDisconnectedException) { }
            catch (JSException) { }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override void OnInitialized()
    {
        Settings.CopyFormatChanged += OnSettingsChanged;
        MenuService.StateChanged += OnMenuServiceStateChanged;
        MenuService.NavigateBarRequested += OnNavigateBarRequested;

        _bars.AddRange(BuildTopLevel());
        _barElements = new ChromelessButton?[_bars.Count];

        _ = PrewarmOtherLogNamesAsync();

        base.OnInitialized();
    }

    private IReadOnlyList<MenuItem> BuildEdit()
    {
        var defaultCopyFormat = Settings.CopyFormat;

        return
        [
            MenuItem.Item(Localizer["Menu_Edit_CopySelected"],
                () => Actions.CopySelectedAsync(EventCopyFormat.Default),
                defaultCopyFormat == EventCopyFormat.Default ? Localizer["Menu_Shortcut_Copy"].Value : null),
            MenuItem.Item(Localizer["Menu_Edit_CopySelectedSimple"],
                () => Actions.CopySelectedAsync(EventCopyFormat.Simple),
                defaultCopyFormat == EventCopyFormat.Simple ? Localizer["Menu_Shortcut_Copy"].Value : null),
            MenuItem.Item(Localizer["Menu_Edit_CopySelectedXml"],
                () => Actions.CopySelectedAsync(EventCopyFormat.Xml),
                defaultCopyFormat == EventCopyFormat.Xml ? Localizer["Menu_Shortcut_Copy"].Value : null),
            MenuItem.Item(Localizer["Menu_Edit_CopySelectedFull"],
                () => Actions.CopySelectedAsync(EventCopyFormat.Full),
                defaultCopyFormat == EventCopyFormat.Full ? Localizer["Menu_Shortcut_Copy"].Value : null),
            MenuItem.Item(Localizer["Menu_Edit_CopySelectedMarkdown"],
                () => Actions.CopySelectedAsync(EventCopyFormat.Markdown),
                defaultCopyFormat == EventCopyFormat.Markdown ? Localizer["Menu_Shortcut_Copy"].Value : null),
        ];
    }

    private IReadOnlyList<MenuItem> BuildFile()
    {
        bool hasActiveLogs = LogTableQueries.HasActiveLogs();

        return
        [
            MenuItem.SubMenu(Localizer["Menu_File_Open"], BuildOpenSubMenu(false)),
            MenuItem.SubMenu(Localizer["Menu_File_Combine"], BuildOpenSubMenu(true), hasActiveLogs),
            MenuItem.Separator(),
            MenuItem.Item(Localizer["Menu_File_CloseAll"], ConfirmCloseAllLogsAsync, isEnabled: hasActiveLogs),
            MenuItem.Separator(),
            MenuItem.Item(Localizer["Menu_File_ExportCsv"],
                () => Actions.ExportEventsAsync(ExportFormat.Csv),
                isEnabled: hasActiveLogs),
            MenuItem.Item(Localizer["Menu_File_ExportJson"],
                () => Actions.ExportEventsAsync(ExportFormat.Json),
                isEnabled: hasActiveLogs),
            MenuItem.Item(Localizer["Menu_File_Exit"], Actions.Exit),
        ];
    }

    private IReadOnlyList<MenuItem> BuildHelp() =>
    [
        MenuItem.Item(Localizer["Menu_Help_Docs"], () => Actions.OpenDocsAsync()),
        MenuItem.Item(Localizer["Menu_Help_SubmitIssue"], () => Actions.OpenIssueAsync()),
        MenuItem.Item(Localizer["Menu_Help_CheckForUpdates"], () => Actions.CheckForUpdatesAsync()),
        MenuItem.Item(Localizer["Menu_Help_ReleaseNotes"], () => Actions.ShowReleaseNotesAsync()),
        MenuItem.Item(Localizer["Menu_Help_ViewLogs"], () => Actions.ShowDebugLogsAsync()),
    ];

    private IReadOnlyList<MenuItem> BuildOpenSubMenu(bool combineLog)
    {
        return
        [
            MenuItem.Item(Localizer["Menu_Open_File"],
                () => Actions.OpenFileAsync(combineLog),
                combineLog ? null : Localizer["Menu_Shortcut_Open"].Value),
            MenuItem.Item(Localizer["Menu_Open_Folder"],
                () => Actions.OpenFolderAsync(combineLog, includeSubfolders: true)),
            MenuItem.Item(Localizer["Menu_Open_FolderTopLevel"],
                () => Actions.OpenFolderAsync(combineLog, includeSubfolders: false)),
            MenuItem.SubMenu(Localizer["Menu_Open_Live"],
            [
                MenuItem.Item(LogChannelNames.ApplicationLog,
                    () => Actions.OpenLiveLogAsync(LogChannelNames.ApplicationLog, combineLog),
                    statusText: ReadinessStatusText(LogChannelNames.ApplicationLog)),
                MenuItem.Item(LogChannelNames.SystemLog,
                    () => Actions.OpenLiveLogAsync(LogChannelNames.SystemLog, combineLog),
                    statusText: ReadinessStatusText(LogChannelNames.SystemLog)),
                MenuItem.Item(LogChannelNames.SecurityLog,
                    () => Actions.OpenLiveLogAsync(LogChannelNames.SecurityLog, combineLog),
                    statusText: ReadinessStatusText(LogChannelNames.SecurityLog)),
                MenuItem.AsyncSubMenu(
                    Localizer["Menu_Open_OtherLogs"],
                    async () => BuildOtherLogsTree(await GetOtherLogReadinessAsync(), combineLog)),
            ]),
        ];
    }

    private IReadOnlyList<MenuItem> BuildOtherLogsTree(
        IReadOnlyList<ChannelReadiness> channelReadiness,
        bool combineLog)
    {
        var rootChildren = new List<MenuItem>();
        var folderMap = new Dictionary<string, List<MenuItem>>(StringComparer.OrdinalIgnoreCase);

        foreach (var readiness in channelReadiness)
        {
            var logName = readiness.Channel;
            var path = LogChannelMethods.GetMenuPath(logName);

            if (path.Count == 0) { continue; }

            var log = path[^1];

            var logMenuItem = MenuItem.Item(log,
                () => Actions.OpenLiveLogAsync(logName, combineLog),
                statusText: ReadinessStatusText(readiness));

            if (path.Count == 1)
            {
                rootChildren.Add(logMenuItem);

                continue;
            }

            var children = rootChildren;
            var pathBuilder = new StringBuilder();

            for (int folderIndex = 0; folderIndex < path.Count - 1; folderIndex++)
            {
                pathBuilder.Append(path[folderIndex]).Append('\0');
                var pathSoFar = pathBuilder.ToString();

                if (folderMap.TryGetValue(pathSoFar, out var existingChildren))
                {
                    children = existingChildren;
                    continue;
                }

                var newChildren = new List<MenuItem>();
                folderMap[pathSoFar] = newChildren;
                children.Add(MenuItem.SubMenu(path[folderIndex], newChildren));
                children = newChildren;
            }

            children.Add(logMenuItem);
        }

        return rootChildren;
    }

    private IReadOnlyList<MenuItem> BuildTools() =>
    [
        MenuItem.Item(Localizer["Menu_Tools_Databases"], () => Actions.OpenDatabaseToolsAsync()),
        MenuItem.Separator(),
        MenuItem.Item(Localizer["Menu_Tools_Settings"], () => Actions.OpenSettingsAsync()),
    ];

    private List<TopLevel> BuildTopLevel() =>
    [
        new(Localizer["Menu_File"], BuildFile),
        new(Localizer["Menu_Edit"], BuildEdit),
        new(Localizer["Menu_View"], BuildView),
        new(Localizer["Menu_Tools"], BuildTools),
        new(Localizer["Menu_Help"], BuildHelp),
    ];

    private IReadOnlyList<MenuItem> BuildView()
    {
        bool isFilterEnabled = FilterPaneQueries.IsEnabled();
        bool isContinuouslyUpdating = EventLogQueries.IsContinuouslyUpdating();
        bool isGrouping = LogTableQueries.IsGrouping();
        bool isGroupDescending = LogTableQueries.IsGroupDescending();
        bool isHistogramVisible = HistogramVisibility.IsVisible;
        bool hasActiveLogs = LogTableQueries.HasActiveLogs();

        return
        [
            MenuItem.Item(
                Localizer["Menu_View_ShowAllEvents"],
                Actions.ToggleShowAllEvents,
                Localizer["Menu_Shortcut_ShowAll"],
                !isFilterEnabled),
            MenuItem.Item(Localizer["Menu_View_LoadNewEvents"], Actions.LoadNewEvents),
            MenuItem.Item(
                Localizer["Menu_View_ContinuouslyUpdate"],
                () => Actions.SetContinuouslyUpdate(!isContinuouslyUpdating),
                isChecked: isContinuouslyUpdating),
            MenuItem.Separator(),
            MenuItem.Item(
                Localizer["Menu_View_GroupDescending"],
                Actions.ToggleGroupSortDirection,
                isChecked: isGroupDescending,
                isEnabled: isGrouping,
                disabledReason: isGrouping ? null : GroupDisabledReason),
            MenuItem.Item(
                Localizer["Menu_View_ExpandAllGroups"],
                () => Actions.SetAllGroupsCollapsed(false),
                isEnabled: isGrouping,
                disabledReason: isGrouping ? null : GroupDisabledReason),
            MenuItem.Item(
                Localizer["Menu_View_CollapseAllGroups"],
                () => Actions.SetAllGroupsCollapsed(true),
                isEnabled: isGrouping,
                disabledReason: isGrouping ? null : GroupDisabledReason),
            MenuItem.Separator(),
            MenuItem.Item(
                Localizer["Menu_View_Timeline"],
                () => Actions.SetHistogramVisible(!isHistogramVisible),
                isChecked: isHistogramVisible),
            MenuItem.Item(
                Localizer["Menu_View_ResolutionCoverage"],
                () => Actions.ShowResolutionCoverageAsync(),
                isEnabled: hasActiveLogs,
                disabledReason: hasActiveLogs ? null : Localizer["Menu_ResolutionCoverageDisabledReason"].Value),
        ];
    }

    private async Task ConfirmCloseAllLogsAsync()
    {
        if (await CloseAllLogsConfirmation.ConfirmAsync(AlertDialogService, Localizer))
        {
            await Actions.CloseAllLogsAsync();
        }
    }

    private async Task<IReadOnlyList<ChannelReadiness>> GetOtherLogReadinessAsync()
    {
        var snapshot = await ChannelReadinessService.GetReadinessAsync();

        var logNames = snapshot
            .Select(channel => channel.Channel)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var readiness = await ChannelReadinessService.GetReadinessAsync(logNames);

        var readinessByChannel = new Dictionary<string, ChannelReadiness>(StringComparer.OrdinalIgnoreCase);

        foreach (var channel in snapshot) { readinessByChannel[channel.Channel] = channel; }

        foreach (var channel in readiness) { readinessByChannel[channel.Channel] = channel; }

        _readinessByChannel = readinessByChannel;

        return [.. readiness.OrderBy(channel => channel.Channel, StringComparer.OrdinalIgnoreCase)];
    }

    private void InvalidatePendingOpen() => Interlocked.Increment(ref _openRequestId);

    private bool IsActive(TopLevel bar) =>
        ActiveBar is not null && ReferenceEquals(ActiveBar, bar) && MenuService.ActiveItems is not null;

    private async Task MoveBarFocus(int currentIndex, int direction, bool openIfMenuActive)
    {
        if (_bars.Count == 0) { return; }

        int newIndex = (((currentIndex + direction) % _bars.Count) + _bars.Count) % _bars.Count;

        await MoveBarFocusTo(newIndex, openIfMenuActive);
    }

    private async Task MoveBarFocusTo(int index, bool openIfMenuActive)
    {
        if (index < 0 || index >= _bars.Count) { return; }

        _focusedBarIndex = index;

        if (openIfMenuActive && MenuService.ActiveItems is not null)
        {
            await OpenBarAsync(_bars[index], index, captureOpener: false);

            return;
        }

        StateHasChanged();

        if (_barElements[index] is not { } barButton) { return; }

        try { await barButton.FocusAsync(); }
        catch { /* element may not be in the DOM yet */ }
    }

    private async Task OnBarClick(TopLevel bar, int index)
    {
        if (IsActive(bar))
        {
            ActiveBar = null;
            MenuService.Close();

            return;
        }

        await OpenBarAsync(bar, index);
    }

    private async Task OnBarHover(TopLevel bar, int index)
    {
        if (MenuService.ActiveItems is null || ReferenceEquals(ActiveBar, bar)) { return; }

        await OpenBarAsync(bar, index, captureOpener: false);
    }

    private async Task OnBarKeyDown(KeyboardEventArgs args, int index)
    {
        if (args.Repeat) { return; }

        switch (args.Key)
        {
            case "ArrowRight":
                await MoveBarFocus(index, +1, true);
                return;
            case "ArrowLeft":
                await MoveBarFocus(index, -1, true);
                return;
            case "Home":
                await MoveBarFocusTo(0, true);
                return;
            case "End":
                await MoveBarFocusTo(_bars.Count - 1, true);
                return;
            case "ArrowDown":
                await OpenBarAsync(_bars[index], index);
                return;
            case "ArrowUp":
                await OpenBarAsync(_bars[index], index, false);
                return;
            case "Escape":
                if (MenuService.ActiveItems is not null) { MenuService.Close(); }

                return;
        }
    }

    private void OnMenuServiceStateChanged()
    {
        if (MenuService.ActiveItems is null)
        {
            InvalidatePendingOpen();

            if (ActiveBar is not null) { ActiveBar = null; }
        }

        _ = InvokeAsync(StateHasChanged);
    }

    private void OnNavigateBarRequested(int direction) =>
        _ = InvokeAsync(() => MoveBarFocus(_focusedBarIndex, direction, true));

    private void OnSettingsChanged() => _ = InvokeAsync(StateHasChanged);

    private async Task OpenBarAsync(TopLevel bar, int index, bool focusFirst = true, bool captureOpener = true)
    {
        var requestId = Interlocked.Increment(ref _openRequestId);

        _menuAnchorModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>(
            "import",
            "./_content/EventLogExpert.UI/Menu/MenuAnchor.js");

        if (_barElements[index] is not { } barButton) { return; }

        var rect = await _menuAnchorModule.InvokeAsync<MenuAnchorRect>(
            "getMenuElementRect",
            barButton.Element);

        if (requestId != Volatile.Read(ref _openRequestId)) { return; }

        ActiveBar = bar;
        _focusedBarIndex = index;
        MenuService.OpenAt(rect.Left, rect.Bottom, bar.BuildItems(), focusFirst, captureOpener);
    }

    private async Task PrewarmOtherLogNamesAsync()
    {
        try
        {
            var readiness = await ChannelReadinessService.GetReadinessAsync();

            _readinessByChannel = readiness.ToDictionary(
                channel => channel.Channel,
                StringComparer.OrdinalIgnoreCase);

            await InvokeAsync(StateHasChanged);
        }
        catch (Exception exception)
        {
            _ = exception;
        }
    }

    private string? ReadinessStatusText(ChannelReadiness readiness) =>
        readiness.Access == ChannelAccess.RequiresElevation ? Localizer["Menu_Status_Elevate"].Value :
        readiness.Enablement == ChannelEnablement.Disabled ? Localizer["Menu_Status_Disabled"].Value : null;

    private string? ReadinessStatusText(string channel) =>
        _readinessByChannel.TryGetValue(channel, out var readiness) ?
            ReadinessStatusText(readiness) : null;

    private sealed record TopLevel(string Label, Func<IReadOnlyList<MenuItem>> BuildItems);
}
