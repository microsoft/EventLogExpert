// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Alerts;
using EventLogExpert.UI.DatabaseTools.Tabs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace EventLogExpert.UI.DatabaseTools;

public sealed partial class DatabaseToolsModal
{
    private static readonly (DatabaseToolsTab Tab, string Label)[] s_tabs =
    [
        (DatabaseToolsTab.Show, "Show Providers"),
        (DatabaseToolsTab.Create, "Create Database"),
        (DatabaseToolsTab.Merge, "Merge Databases"),
        (DatabaseToolsTab.Diff, "Diff Databases"),
        (DatabaseToolsTab.Upgrade, "Upgrade Database")
    ];

    private readonly Dictionary<DatabaseToolsTab, ElementReference> _tabButtonRefs = new();

    private DatabaseToolsTab _activeTab = DatabaseToolsTab.Show;
    private CreateDatabaseTab? _createTab;
    private DiffDatabasesTab? _diffTab;
    private bool _isArrowOrHomeEnd;
    private MergeDatabaseTab? _mergeTab;
    private DatabaseToolsTab? _pendingFocusTab;
    private ShowProvidersTab? _showTab;
    private UpgradeDatabaseTab? _upgradeTab;
    private bool _verboseLogging;

    /// <summary>True when any tab is mid-Run (so the modal close path must confirm cancel first).</summary>
    private bool AnyTabIsRunning =>
        (_showTab?.IsRunning ?? false) ||
        (_createTab?.IsRunning ?? false) ||
        (_mergeTab?.IsRunning ?? false) ||
        (_diffTab?.IsRunning ?? false) ||
        (_upgradeTab?.IsRunning ?? false);

    private bool IsArrowOrHomeEnd => _isArrowOrHomeEnd;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_pendingFocusTab is { } tab && _tabButtonRefs.TryGetValue(tab, out var elementRef))
        {
            _pendingFocusTab = null;
            try { await elementRef.FocusAsync(); }
            catch { /* Best-effort: element may not be in the DOM yet. */ }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override async Task OnCancelAsync()
    {
        if (AnyTabIsRunning)
        {
            var confirm = await ShowInlineAlertAsync(
                new InlineAlertRequest(
                    Title: "Operation in progress",
                    Message: "An operation is running. Cancel and close anyway?",
                    AcceptLabel: "Cancel and close",
                    CancelLabel: "Continue running",
                    IsPrompt: false,
                    PromptInitialValue: null),
                CancellationToken.None);

            if (!confirm.Accepted) { return; }

            // Best-effort: cancel running tabs before closing.
            _showTab?.CancelIfRunning();
            _createTab?.CancelIfRunning();
            _mergeTab?.CancelIfRunning();
            _diffTab?.CancelIfRunning();
            _upgradeTab?.CancelIfRunning();
        }

        await base.OnCancelAsync();
    }

    private static DatabaseToolsTab NextTab(DatabaseToolsTab current)
    {
        var index = Array.FindIndex(s_tabs, t => t.Tab == current);

        return index < s_tabs.Length - 1 ? s_tabs[index + 1].Tab : s_tabs[0].Tab;
    }

    private static DatabaseToolsTab PrevTab(DatabaseToolsTab current)
    {
        var index = Array.FindIndex(s_tabs, t => t.Tab == current);

        return index > 0 ? s_tabs[index - 1].Tab : s_tabs[^1].Tab;
    }

    private void OnTabKeyDown(KeyboardEventArgs e, DatabaseToolsTab tab)
    {
        _isArrowOrHomeEnd = e.Key is "ArrowDown" or "ArrowUp" or "ArrowLeft" or "ArrowRight" or "Home" or "End";

        switch (e.Key)
        {
            case "ArrowDown":
            case "ArrowRight":
                SetActiveTab(NextTab(tab));
                break;
            case "ArrowUp":
            case "ArrowLeft":
                SetActiveTab(PrevTab(tab));
                break;
            case "Home":
                SetActiveTab(s_tabs[0].Tab);
                break;
            case "End":
                SetActiveTab(s_tabs[^1].Tab);
                break;
        }
    }

    private void SetActiveTab(DatabaseToolsTab tab)
    {
        if (_activeTab == tab) { return; }

        _activeTab = tab;
        _pendingFocusTab = tab;
    }
}
