// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.UI.DatabaseTools.Tabs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace EventLogExpert.UI.DatabaseTools;

public sealed partial class DatabaseToolsModal : IInlineAlertSurface
{
    private static readonly (DatabaseToolsTab Tab, string Label)[] s_tabs =
    [
        (DatabaseToolsTab.Manage, "Manage"),
        (DatabaseToolsTab.Show, "Show Providers"),
        (DatabaseToolsTab.Create, "Create Database"),
        (DatabaseToolsTab.Merge, "Merge Databases"),
        (DatabaseToolsTab.Diff, "Diff Databases"),
        (DatabaseToolsTab.Upgrade, "Upgrade Database")
    ];

    private readonly Dictionary<DatabaseToolsTab, ElementReference> _tabButtonRefs = new();

    private DatabaseToolsTab _activeTab = DatabaseToolsTab.Manage;
    private CreateDatabaseTab? _createTab;
    private DiffDatabasesTab? _diffTab;
    private ManageDatabasesTab? _manageTab;
    private MergeDatabaseTab? _mergeTab;
    private DatabaseToolsTab? _pendingFocusTab;
    private ShowProvidersTab? _showTab;
    private IJSObjectReference? _tabKeyModule;
    private ElementReference _tablistRef;
    private UpgradeDatabaseTab? _upgradeTab;
    private bool _verboseLogging;

    /// <summary>True when any tab is mid-Run (so the modal close path must confirm cancel first).</summary>
    private bool AnyTabIsRunning =>
        (_showTab?.IsRunning ?? false) ||
        (_createTab?.IsRunning ?? false) ||
        (_mergeTab?.IsRunning ?? false) ||
        (_diffTab?.IsRunning ?? false) ||
        (_upgradeTab?.IsRunning ?? false);

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private ILogReloadCoordinator LogReloadCoordinator { get; init; } = null!;

    [Inject] private ITraceLogger TraceLogger { get; init; } = null!;

    protected override async ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing && _tabKeyModule is not null)
        {
            try
            {
                await _tabKeyModule.InvokeVoidAsync("detach", _tablistRef);
                await _tabKeyModule.DisposeAsync();
            }
            catch (JSDisconnectedException) { /* Circuit gone — ignore. */ }
            catch (JSException) { /* Stale module/ref — best-effort teardown. */ }
            catch (ObjectDisposedException) { /* Already torn down — ignore. */ }
        }

        await base.DisposeAsyncCore(disposing);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                _tabKeyModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
                    "import",
                    "./_content/EventLogExpert.UI/DatabaseTools/DatabaseToolsModal.js");

                await _tabKeyModule.InvokeVoidAsync("attach", _tablistRef);
            }
            catch (JSDisconnectedException) { /* Closed mid-import — ignore. */ }
            catch (JSException) { /* Stale module/ref — best-effort keyboard nav. */ }
        }

        if (_pendingFocusTab is { } tab && _tabButtonRefs.TryGetValue(tab, out var elementRef))
        {
            _pendingFocusTab = null;
            try { await elementRef.FocusAsync(); }
            catch { /* Best-effort: element may not be in the DOM yet. */ }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override async Task OnClosingAsync()
    {
        // CancelIfRunning is a no-op when not running; safe to call from all close paths.
        // Manage tab is not a DatabaseToolsTabBase<TRequest> and has no Run/Cancel surface.
        _showTab?.CancelIfRunning();
        _createTab?.CancelIfRunning();
        _mergeTab?.CancelIfRunning();
        _diffTab?.CancelIfRunning();
        _upgradeTab?.CancelIfRunning();

        if (_manageTab is { HasDatabaseStateChanged: true })
        {
            await PromptAndReloadOpenLogs();
        }

        await base.OnClosingAsync();
    }

    protected override async Task<bool> OnRequestCloseAsync(ModalCloseRequest request)
    {
        // 1. Hard-block while Manage-tab-initiated upgrade is in flight.
        if (_manageTab is { IsUpgradeInFlight: true }) { return false; }

        // 2. Pending toggles → save prompt OR discard prompt.
        if (_manageTab is { HasPendingChanges: true })
        {
            var savePrompt = await ShowInlineAlertAsync(
                new InlineAlertRequest(
                    Title: "Unsaved changes",
                    Message: "You have pending database changes. Save them now?",
                    AcceptLabel: "Save",
                    CancelLabel: "Don't save",
                    IsPrompt: false,
                    PromptInitialValue: null),
                CancellationToken.None);

            if (savePrompt.Accepted)
            {
                var saved = await _manageTab.ApplyPendingTogglesAsync();
                if (!saved) { return false; }
            }
            else
            {
                var closeAnyway = await ShowInlineAlertAsync(
                    new InlineAlertRequest(
                        Title: "Discard changes?",
                        Message: "Close without saving? Pending changes will be lost.",
                        AcceptLabel: "Close",
                        CancelLabel: "Stay open",
                        IsPrompt: false,
                        PromptInitialValue: null),
                    CancellationToken.None);

                if (!closeAnyway.Accepted) { return false; }
            }

            // Re-check after the async prompt round-trip.
            if (_manageTab is { IsUpgradeInFlight: true }) { return false; }
        }

        // 3. Existing AnyTabIsRunning prompt — preserves current behavior.
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

            if (!confirm.Accepted) { return false; }
        }

        // 4. Final upgrade re-check covers upgrades started during the AnyTabIsRunning prompt.
        if (_manageTab is { IsUpgradeInFlight: true }) { return false; }

        return true;
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

    private async Task PromptAndReloadOpenLogs()
    {
        if (!LogReloadCoordinator.HasActiveLogs) { return; }

        bool yes;

        try
        {
            var result = await ShowInlineAlertAsync(
                new InlineAlertRequest(
                    Title: "Reload Open Logs Now?",
                    Message: "In order for these changes to take effect, all currently open logs must be reloaded. " +
                        "Would you like to reload all open logs now?",
                    AcceptLabel: "Yes",
                    CancelLabel: "No",
                    IsPrompt: false,
                    PromptInitialValue: null),
                CancellationToken.None);

            yes = result.Accepted;
        }
        catch (ObjectDisposedException) { return; }
        catch (OperationCanceledException) { return; }

        if (yes)
        {
            try { await LogReloadCoordinator.ReloadAllActiveLogsAsync(); }
            catch (OperationCanceledException) { }
            catch (TimeoutException ex)
            {
                TraceLogger.Warning(
                    $"{nameof(DatabaseToolsModal)}.{nameof(PromptAndReloadOpenLogs)}: reload did not complete within timeout: {ex}");
            }
        }
    }

    private void SetActiveTab(DatabaseToolsTab tab)
    {
        if (_activeTab == tab) { return; }

        _activeTab = tab;
        _pendingFocusTab = tab;
    }
}
