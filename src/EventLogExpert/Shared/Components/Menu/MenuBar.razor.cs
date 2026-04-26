// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.FilterPane;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Text;

namespace EventLogExpert.Shared.Components.Menu;

public sealed partial class MenuBar : IDisposable
{
    private readonly List<TopLevel> _bars = [];
    private ElementReference[] _barElements = [];
    private int _focusedBarIndex;

    [Inject] private IMenuActionService Actions { get; init; } = null!;

    private TopLevel? ActiveBar { get; set; }

    [Inject]
    private IStateSelection<EventLogState, bool> ContinuouslyUpdate { get; init; } = null!;

    [Inject]
    private IStateSelection<FilterPaneState, bool> FilterPaneIsEnabled { get; init; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private IMenuService MenuService { get; init; } = null!;

    [Inject] private ISettingsService Settings { get; init; } = null!;

    public void Dispose()
    {
        Settings.CopyTypeChanged -= OnSettingsChanged;
        MenuService.StateChanged -= OnMenuServiceStateChanged;
        MenuService.NavigateBarRequested -= OnNavigateBarRequested;
    }

    protected override void OnInitialized()
    {
        ContinuouslyUpdate.Select(state => state.ContinuouslyUpdate);
        FilterPaneIsEnabled.Select(state => state.IsEnabled);

        Settings.CopyTypeChanged += OnSettingsChanged;
        MenuService.StateChanged += OnMenuServiceStateChanged;
        MenuService.NavigateBarRequested += OnNavigateBarRequested;

        _bars.AddRange(BuildTopLevel());
        _barElements = new ElementReference[_bars.Count];

        _ = PrewarmOtherLogNamesAsync();

        base.OnInitialized();
    }

    private IReadOnlyList<MenuItem> BuildEdit()
    {
        var copyShortcut = Settings.CopyType;

        return
        [
            MenuItem.Item("Copy Selected",
                () => Actions.CopySelectedAsync(CopyType.Default),
                copyShortcut == CopyType.Default ? "Ctrl+C" : null),
            MenuItem.Item("Copy Selected (Simple)",
                () => Actions.CopySelectedAsync(CopyType.Simple),
                copyShortcut == CopyType.Simple ? "Ctrl+C" : null),
            MenuItem.Item("Copy Selected (XML)",
                () => Actions.CopySelectedAsync(CopyType.Xml),
                copyShortcut == CopyType.Xml ? "Ctrl+C" : null),
            MenuItem.Item("Copy Selected (Full)",
                () => Actions.CopySelectedAsync(CopyType.Full),
                copyShortcut == CopyType.Full ? "Ctrl+C" : null),
            MenuItem.Separator(),
            MenuItem.Item("Save All Filters", () => Actions.SaveAllFiltersAsync()),
            MenuItem.Item("Clear All Filters", Actions.ClearAllFilters),
        ];
    }

    private IReadOnlyList<MenuItem> BuildFile() =>
    [
        MenuItem.SubMenu("Open", BuildOpenSubMenu(false)),
        MenuItem.SubMenu("Add Another Log To This View", BuildOpenSubMenu(true)),
        MenuItem.Separator(),
        MenuItem.Item("Close All Open Logs", () => Actions.CloseAllLogsAsync()),
        MenuItem.Item("Exit", Actions.Exit),
    ];

    private IReadOnlyList<MenuItem> BuildHelp() =>
    [
        MenuItem.Item("Docs", () => Actions.OpenDocsAsync()),
        MenuItem.Item("Submit an Issue", () => Actions.OpenIssueAsync()),
        MenuItem.Item("Check for Updates", () => Actions.CheckForUpdatesAsync()),
        MenuItem.Item("Release Notes", () => Actions.ShowReleaseNotesAsync()),
        MenuItem.Item("View Logs", () => Actions.ShowDebugLogsAsync()),
    ];

    private IReadOnlyList<MenuItem> BuildOpenSubMenu(bool addLog) =>
    [
        MenuItem.Item("File", () => Actions.OpenFileAsync(addLog), addLog ? null : "Ctrl+O"),
        MenuItem.Item("Folder", () => Actions.OpenFolderAsync(addLog)),
        MenuItem.SubMenu("Live Event Log",
        [
            MenuItem.Item("Application", () => Actions.OpenLiveLogAsync("Application", addLog)),
            MenuItem.Item("System", () => Actions.OpenLiveLogAsync("System", addLog)),
            MenuItem.Item("Security", () => Actions.OpenLiveLogAsync("Security", addLog)),
            MenuItem.AsyncSubMenu(
                "Other Logs",
                async () => BuildOtherLogsTree(await Actions.GetOtherLogNamesAsync(), addLog)),
        ]),
    ];

    private IReadOnlyList<MenuItem> BuildOtherLogsTree(IReadOnlyList<string> logNames, bool addLog)
    {
        var rootChildren = new List<MenuItem>();
        var folderMap = new Dictionary<string, List<MenuItem>>(StringComparer.OrdinalIgnoreCase);

        foreach (var logName in logNames)
        {
            var path = LogNameMethods.GetMenuPath(logName);

            if (path.Count == 0) { continue; }

            var leafLabel = path[^1];
            var leaf = MenuItem.Item(leafLabel, () => Actions.OpenLiveLogAsync(logName, addLog));

            if (path.Count == 1)
            {
                rootChildren.Add(leaf);

                continue;
            }

            // NUL keys folderMap entries so segments containing '-' or '/' can't collide.
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

            children.Add(leaf);
        }

        return rootChildren;
    }

    private IReadOnlyList<MenuItem> BuildTools() => [MenuItem.Item("Settings", () => Actions.OpenSettingsAsync()),];

    private List<TopLevel> BuildTopLevel() =>
    [
        new("File", BuildFile),
        new("Edit", BuildEdit),
        new("View", BuildView),
        new("Tools", BuildTools),
        new("Help", BuildHelp),
    ];

    private IReadOnlyList<MenuItem> BuildView()
    {
        // Snapshot state at open time. Live updates aren't pushed into an open menu — the next open will reflect any change.
        bool isFilterEnabled = FilterPaneIsEnabled.Value;
        bool isContinuouslyUpdating = ContinuouslyUpdate.Value;

        return
        [
            MenuItem.Item(
                "Show All Events",
                Actions.ToggleShowAllEvents,
                "Ctrl+H",
                isChecked: !isFilterEnabled),
            MenuItem.Item("Load New Events", Actions.LoadNewEvents), MenuItem.Item(
                "Continuously Update",
                () => Actions.SetContinuouslyUpdate(!isContinuouslyUpdating),
                isChecked: isContinuouslyUpdating),
            MenuItem.Separator(), MenuItem.Item("Show Cached Filters", () => Actions.ShowFilterCacheAsync()),
            MenuItem.Item("Show Filter Groups", () => Actions.ShowFilterGroupsAsync()),
        ];
    }

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

        // If a menu is already open, switch to the new bar's menu so arrow keys feel continuous.
        if (openIfMenuActive && MenuService.ActiveItems is not null)
        {
            // Preserve the original opener so closing restores focus to the menubar.
            await OpenBarAsync(_bars[index], index, captureOpener: false);

            return;
        }

        StateHasChanged();

        try { await _barElements[index].FocusAsync(); }
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
        // Only switch on hover when another menu is already open — matches Win32 menubar behavior.
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
                // Enter/Space are intentionally not handled here so the browser's native button
                // click fires once — handling them on keydown would toggle the menu shut.
                await OpenBarAsync(_bars[index], index, true);
                return;
            case "ArrowUp":
                // WAI-ARIA menubar: ArrowUp opens and focuses the last item.
                await OpenBarAsync(_bars[index], index, false);
                return;
            case "Escape":
                if (MenuService.ActiveItems is not null) { MenuService.Close(); }

                return;
        }
    }

    private void OnMenuServiceStateChanged()
    {
        if (MenuService.ActiveItems is null && ActiveBar is not null)
        {
            ActiveBar = null;
        }

        _ = InvokeAsync(StateHasChanged);
    }

    private void OnNavigateBarRequested(int direction) =>
        _ = InvokeAsync(() => MoveBarFocus(_focusedBarIndex, direction, true));

    private void OnSettingsChanged() => _ = InvokeAsync(StateHasChanged);

    private async Task OpenBarAsync(TopLevel bar, int index, bool focusFirst = true, bool captureOpener = true)
    {
        // Anchor the dropdown to the bottom-left of the trigger button.
        var rect = await JSRuntime.InvokeAsync<MenuBarItemRect>(
            "getMenuElementRect",
            _barElements[index]);

        ActiveBar = bar;
        _focusedBarIndex = index;
        MenuService.OpenAt(rect.Left, rect.Bottom, bar.BuildItems(), focusFirst, captureOpener);
    }

    private async Task PrewarmOtherLogNamesAsync()
    {
        try { await Actions.GetOtherLogNamesAsync(); }
        catch { /* prewarm best-effort */ }
    }

    private sealed record TopLevel(string Label, Func<IReadOnlyList<MenuItem>> BuildItems);

    private sealed record MenuBarItemRect(
        double Left,
        double Top,
        double Right,
        double Bottom,
        double Width,
        double Height);
}
