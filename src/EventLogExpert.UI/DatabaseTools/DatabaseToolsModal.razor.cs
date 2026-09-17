// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.UI.Alerts;
using EventLogExpert.UI.DatabaseTools.Tabs;
using EventLogExpert.UI.Modal;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.DatabaseTools;

public sealed partial class DatabaseToolsModal : IInlineAlertSurface
{
    private static readonly DatabaseToolsTab[] s_tabs =
    [
        DatabaseToolsTab.Manage,
        DatabaseToolsTab.Show,
        DatabaseToolsTab.Create,
        DatabaseToolsTab.Merge,
        DatabaseToolsTab.Diff,
        DatabaseToolsTab.Upgrade
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

    [Inject] private IStringLocalizer<SharedResource> Localizer { get; init; } = null!;

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
                    Title: Localizer["DatabaseTools_Unsaved_Title"],
                    Message: Localizer["DatabaseTools_Unsaved_Message"],
                    AcceptLabel: Localizer["Modal_Save"],
                    CancelLabel: Localizer["DatabaseTools_Unsaved_DontSave"],
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
                        Title: Localizer["DatabaseTools_Discard_Title"],
                        Message: Localizer["DatabaseTools_Discard_Message"],
                        AcceptLabel: Localizer["Modal_Close"],
                        CancelLabel: Localizer["DatabaseTools_Discard_StayOpen"],
                        IsPrompt: false,
                        PromptInitialValue: null),
                    CancellationToken.None);

                if (!closeAnyway.Accepted) { return false; }
            }

            if (_manageTab is { IsUpgradeInFlight: true }) { return false; }
        }

        if (AnyTabIsRunning)
        {
            var confirm = await ShowInlineAlertAsync(
                new InlineAlertRequest(
                    Title: Localizer["DatabaseTools_Running_Title"],
                    Message: Localizer["DatabaseTools_Running_Message"],
                    AcceptLabel: Localizer["DatabaseTools_Running_CancelAndClose"],
                    CancelLabel: Localizer["DatabaseTools_Running_ContinueRunning"],
                    IsPrompt: false,
                    PromptInitialValue: null),
                CancellationToken.None);

            if (!confirm.Accepted) { return false; }
        }

        if (_manageTab is { IsUpgradeInFlight: true }) { return false; }

        return true;
    }

    private async Task<bool> AskOverwriteAsync(string fileName, CancellationToken cancellationToken)
    {
        try
        {
            var result = await ShowInlineAlertAsync(
                new InlineAlertRequest(
                    Title: Localizer["DatabaseTools_DbExists_Title"],
                    Message: Localizer["DatabaseTools_DbExists_OverwriteMessage", fileName],
                    AcceptLabel: Localizer["DatabaseTools_Action_Overwrite"],
                    CancelLabel: Localizer["DatabaseTools_Action_Skip"],
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
                throw new AutoImportIncompleteException();
            }

            AnnouncementService.Announce(enable ?
                Localizer["DatabaseTools_Announcement_DatabaseImportedEnabled"] :
                Localizer["DatabaseTools_Announcement_DatabaseImported"]);

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
                    Title: Localizer["DatabaseTools_Reload_Title"],
                    Message: Localizer["DatabaseTools_Reload_Message"],
                    AcceptLabel: Localizer["DatabaseTools_Action_Yes"],
                    CancelLabel: Localizer["DatabaseTools_Action_No"],
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
                    Title: Localizer["DatabaseTools_ImportFailed_Title"],
                    Message: Localizer["DatabaseTools_ImportFailed_Message", fileName],
                    AcceptLabel: Localizer["Modal_Accept"],
                    CancelLabel: Localizer["Modal_Accept"],
                    IsPrompt: false,
                    PromptInitialValue: null),
                CancellationToken.None);
        }
        catch (ObjectDisposedException) { }
    }
}
