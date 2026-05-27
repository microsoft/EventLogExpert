// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.DetailsPane;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.Modal;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace EventLogExpert.UI.Settings;

public sealed partial class SettingsModal : ModalBase<bool>
{
    private readonly Dictionary<string, bool> _pendingToggles = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _classificationObservationCts;
    private EventCopyFormat _copyFormat;
    private bool _databaseStateChanged;
    private volatile bool _disposed;
    private ElementReference _importButtonRef;
    private bool _isPreReleaseEnabled;
    private LogLevel _logLevel;
    private bool _showDisplayPaneOnSelectionChange;
    private Theme _theme;
    private string _timeZoneId = string.Empty;

    [Inject] private IAnnouncementService AnnouncementService { get; init; } = null!;

    [Inject] private IDatabaseOperationCoordinator Coordinator { get; init; } = null!;

    [Inject] private IDatabaseService DatabaseService { get; init; } = null!;

    [Inject] private IDetailsPanePreferencesProvider DetailsPanePreferences { get; init; } = null!;

    private bool IsAnyUpgradeInFlight =>
        Coordinator.IsAnyUpgradeInFlight || ProgressBannerService.SettingsProgress is not null;

    private bool IsClassificationPending => !DatabaseService.InitialClassificationTask.IsCompleted;

    private bool IsCloseBlocked => IsAnyUpgradeInFlight;

    [Inject] private ILogReloadCoordinator LogReloadCoordinator { get; init; } = null!;

    [Inject] private IProgressBannerService ProgressBannerService { get; init; } = null!;

    [Inject] private ISettingsService Settings { get; init; } = null!;

    [Inject] private ITraceLogger TraceLogger { get; init; } = null!;

    /// <summary>
    ///     Test-only seam. <see cref="SettingsModalTests" /> invoke this instead of routing through the ModalChrome
    ///     footer markup, which would couple tests to chrome button class names.
    /// </summary>
    internal Task InvokeOnSaveAsyncForTests() => OnSaveAsync();

    protected override async ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            _classificationObservationCts?.CancelAsync();
            _classificationObservationCts?.Dispose();
            _classificationObservationCts = null;
            DatabaseService.EntriesChanged -= OnDatabaseEntriesChanged;
            ProgressBannerService.StateChanged -= OnBannerStateChanged;
            Coordinator.UpgradeStateChanged -= OnCoordinatorStateChanged;
        }

        await base.DisposeAsyncCore(disposing);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && !_disposed)
        {
            try
            {
                await _importButtonRef.FocusAsync(preventScroll: true);
            }
            catch (ObjectDisposedException) { /* renderer disposed during dispatch; safe to ignore */ }
            catch (JSException) { /* element absent or detached between dispatch and call */ }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override async Task OnClosingAsync()
    {
        if (_databaseStateChanged)
        {
            await PromptAndReloadOpenLogs();

            _databaseStateChanged = false;
        }
    }

    protected override void OnInitialized()
    {
        LoadFromSettings();

        DatabaseService.EntriesChanged += OnDatabaseEntriesChanged;
        ProgressBannerService.StateChanged += OnBannerStateChanged;
        Coordinator.UpgradeStateChanged += OnCoordinatorStateChanged;

        if (!DatabaseService.InitialClassificationTask.IsCompleted)
        {
            _classificationObservationCts = new CancellationTokenSource();
            _ = ObserveClassificationCompletionAsync(_classificationObservationCts.Token);
        }

        base.OnInitialized();
    }

    protected override Task<bool> OnRequestCloseAsync(ModalCloseRequest request) =>
        Task.FromResult(!IsCloseBlocked);

    protected override async Task OnSaveAsync()
    {
        if (await SaveSettingsAsync())
        {
            AnnouncementService.Announce("Settings saved");

            await CompleteAsync(true);
        }
    }

    private async Task<bool> AskOverwriteAsync(string fileName, CancellationToken cancellationToken)
    {
        if (_disposed) { return false; }

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

    private bool GetEffectiveEnabled(DatabaseEntry entry) =>
        _pendingToggles.TryGetValue(entry.FileName, out var pending) ? pending : entry.IsEnabled;

    private async Task ImportDatabase()
    {
        var outcome = await Coordinator.ImportAsync(AskOverwriteAsync);

        if (_disposed) { return; }

        if (outcome.DatabaseStateChanged && await SaveSettingsAsync())
        {
            AnnouncementService.Announce("Database imported");

            try { await CompleteAsync(true); }
            catch (ObjectDisposedException)
            {
                // Modal torn down between the _disposed check and CompleteAsync; safe to ignore.
            }
        }
    }

    private async Task InvokeAsyncSafe()
    {
        try { await InvokeAsync(StateHasChanged); }
        catch (ObjectDisposedException)
        {
            // Renderer disposed concurrently with this dispatch; safe to ignore during teardown.
        }
    }

    private void LoadFromSettings()
    {
        _copyFormat = Settings.CopyFormat;
        _isPreReleaseEnabled = Settings.IsPreReleaseEnabled;
        _logLevel = Settings.LogLevel;
        _showDisplayPaneOnSelectionChange = DetailsPanePreferences.DisplayPaneSelectionPreference;
        _theme = Settings.Theme;
        _timeZoneId = Settings.TimeZoneId;
    }

    private async Task ObserveClassificationCompletionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await DatabaseService.InitialClassificationTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            // InitialClassificationTask is contractually never-faulting (DatabaseService.cs);
            // defensive try/catch so a future contract drift cannot orphan this fire-and-forget task.
        }

        if (_disposed) { return; }

        await InvokeAsyncSafe();
    }

    private void OnBannerStateChanged() => _ = InvokeAsyncSafe();

    private void OnCoordinatorStateChanged() => _ = InvokeAsyncSafe();

    private void OnDatabaseEntriesChanged(object? sender, EventArgs e)
    {
        _databaseStateChanged = true;
        _ = InvokeAsyncSafe();
    }

    private async Task PromptAndReloadOpenLogs()
    {
        if (!LogReloadCoordinator.HasActiveLogs) { return; }

        if (_disposed) { return; }

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
                    $"{nameof(SettingsModal)}.{nameof(PromptAndReloadOpenLogs)}: reload did not complete within timeout: {ex}");
            }
        }
    }

    private async Task RemoveDatabase(DatabaseEntry entry)
    {
        var fileName = entry.FileName;

        var outcome = await Coordinator.RemoveDatabaseAsync(
            fileName,
            async (showCloseReopenWarning, cancellationToken) =>
            {
                if (_disposed) { return false; }

                var message = showCloseReopenWarning
                    ? $"{fileName} is currently enabled. Removing will close and reopen any affected log views. Are you sure?"
                    : $"Are you sure you want to remove {fileName}?";

                try
                {
                    var result = await ShowInlineAlertAsync(
                        new InlineAlertRequest(
                            Title: "Remove Database",
                            Message: message,
                            AcceptLabel: "Remove",
                            CancelLabel: "Cancel",
                            IsPrompt: false,
                            PromptInitialValue: null),
                        cancellationToken);

                    return result.Accepted;
                }
                catch (ObjectDisposedException) { return false; }
            });

        if (_disposed) { return; }

        if (outcome.Confirmed) { _pendingToggles.Remove(fileName); }

        if (outcome.LogsReopened)
        {
            _databaseStateChanged = false;
        }
        else if (outcome.Removed)
        {
            _databaseStateChanged = true;
        }
    }

    private async Task<bool> SaveSettingsAsync()
    {
        if (IsCloseBlocked) { return false; }

        var snapshot = _pendingToggles.Keys.ToArray();
        _pendingToggles.Clear();

        await Coordinator.ApplyPendingTogglesAsync(snapshot);

        if (_disposed) { return false; }

        Settings.CopyFormat = _copyFormat;
        Settings.IsPreReleaseEnabled = _isPreReleaseEnabled;
        Settings.LogLevel = _logLevel;
        DetailsPanePreferences.DisplayPaneSelectionPreference = _showDisplayPaneOnSelectionChange;
        Settings.Theme = _theme;
        Settings.TimeZoneId = _timeZoneId;

        return true;
    }

    private void ToggleDatabase(string fileName)
    {
        var entry = DatabaseService.Entries.FirstOrDefault(e =>
            string.Equals(e.FileName, fileName, StringComparison.OrdinalIgnoreCase));

        if (entry is null) { return; }

        var newValue = !GetEffectiveEnabled(entry);

        if (newValue == entry.IsEnabled)
        {
            _pendingToggles.Remove(fileName);
        }
        else
        {
            _pendingToggles[fileName] = newValue;
        }
    }

    private async Task UpgradeEntry(string fileName)
    {
        await Coordinator.UpgradeDatabaseAsync(fileName);

        if (_disposed) { return; }

        await InvokeAsyncSafe();
    }
}
