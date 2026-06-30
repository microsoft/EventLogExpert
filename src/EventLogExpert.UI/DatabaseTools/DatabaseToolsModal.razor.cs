// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.UI.DatabaseTools.Tabs;
using Microsoft.AspNetCore.Components;

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
    private readonly CancellationTokenSource _autoImportCts = new();

    private DatabaseToolsTab _activeTab = DatabaseToolsTab.Manage;
    private CreateDatabaseTab? _createTab;
    private DiffDatabasesTab? _diffTab;
    private ManageDatabasesTab? _manageTab;
    private MergeDatabaseTab? _mergeTab;
    private ShowProvidersTab? _showTab;
    private UpgradeDatabaseTab? _upgradeTab;
    private bool _verboseLogging;

    [Inject] private IAnnouncementService AnnouncementService { get; init; } = null!;

    private bool AnyTabIsRunning =>
        (_showTab?.IsRunning ?? false) ||
        (_createTab?.IsRunning ?? false) ||
        (_mergeTab?.IsRunning ?? false) ||
        (_diffTab?.IsRunning ?? false) ||
        (_upgradeTab?.IsRunning ?? false);

    [Inject] private IDatabaseOperationCoordinator DatabaseOperationCoordinator { get; init; } = null!;

    [Inject] private ILogReloadCoordinator LogReloadCoordinator { get; init; } = null!;

    [Inject] private ITraceLogger TraceLogger { get; init; } = null!;

    protected override async ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing)
        {
            await _autoImportCts.CancelAsync();
            _autoImportCts.Dispose();
        }

        await base.DisposeAsyncCore(disposing);
    }

    protected override async Task OnClosingAsync()
    {
        await _autoImportCts.CancelAsync();

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
        if (_activeTab == DatabaseToolsTab.Manage && _manageTab is { IsInSelectionMode: true } manageTab)
        {
            await manageTab.ExitSelectionModeWithFocusAsync();
            return false;
        }

        if (_manageTab is { IsUpgradeInFlight: true }) { return false; }

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

            // Async prompts can race Manage-tab upgrades; re-check before closing.
            if (_manageTab is { IsUpgradeInFlight: true }) { return false; }
        }

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

        // Covers upgrades started during the running-operation prompt.
        if (_manageTab is { IsUpgradeInFlight: true }) { return false; }

        return true;
    }

    private async Task<bool> AskOverwriteAsync(string fileName, CancellationToken cancellationToken)
    {
        try
        {
            var result = await ShowInlineAlertAsync(
                new InlineAlertRequest(
                    Title: "Database already exists",
                    Message: $"{fileName} already exists. Overwrite?",
                    AcceptLabel: "Overwrite",
                    CancelLabel: "Skip",
                    IsPrompt: false,
                    PromptInitialValue: null),
                cancellationToken);

            return result.Accepted;
        }
        catch (ObjectDisposedException) { return false; }
    }

    private async Task HandleAutoImportAsync(string producedPath, bool enable)
    {
        try
        {
            var outcome = await DatabaseOperationCoordinator.ImportPathsAsync(
                [producedPath],
                enableOnImport: enable,
                askOverwriteAsync: AskOverwriteAsync,
                cancellationToken: _autoImportCts.Token);

            if (!outcome.DatabaseStateChanged || outcome.Failures.Count > 0 || outcome.UpgradeFailures.Count > 0)
            {
                throw new InvalidOperationException("The database import did not complete.");
            }

            AnnouncementService.Announce(enable ? "Database imported and enabled" : "Database imported");

            if (enable)
            {
                _activeTab = DatabaseToolsTab.Manage;
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException)
        {
            await ShowAutoImportErrorAsync(Path.GetFileName(producedPath));
            throw;
        }
        catch (Exception ex)
        {
            TraceLogger.Warning($"{nameof(DatabaseToolsModal)}.{nameof(HandleAutoImportAsync)} failed: {ex}");
            await ShowAutoImportErrorAsync(Path.GetFileName(producedPath));
            throw;
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

    private async Task ShowAutoImportErrorAsync(string fileName)
    {
        try
        {
            await ShowInlineAlertAsync(
                new InlineAlertRequest(
                    Title: "Import Failed",
                    Message: $"The database was created, but '{fileName}' was not imported.",
                    AcceptLabel: "OK",
                    CancelLabel: "OK",
                    IsPrompt: false,
                    PromptInitialValue: null),
                CancellationToken.None);
        }
        catch (ObjectDisposedException) { }
    }
}
