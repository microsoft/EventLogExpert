// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.Localization;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.DatabaseTools;
using EventLogExpert.Runtime.DebugLog;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Diagnostics;

namespace EventLogExpert.UI.DatabaseTools.Tabs;

public abstract class DatabaseToolsTabBase<TRequest> : ComponentBase, IDisposable
    where TRequest : class
{
    private const int FlushIntervalMs = 50;

    private readonly List<LogRecord> _pendingEntries = [];
    private readonly Lock _pendingLock = new();

    private string? _autoImportPath;
    private AutoImportState _autoImportState;
    private volatile bool _disposed;
    private bool _flushScheduled;

    // Snapshot at dispatch so post-run import targets the built file, not later target-field edits.
    private string? _producedDatabasePathSnapshot;

    public bool IsConfirming { get; private set; }

    public bool IsRunning { get; protected set; }

    [CascadingParameter(Name = "RequestAutoImport")] public Func<string, bool, Task>? RequestAutoImportAsync { get; set; }

    [CascadingParameter(Name = "VerboseLogging")] public bool VerboseLogging { get; set; }

    protected virtual bool CanRun => true;

    protected CancellationTokenSource? Cts { get; set; }

    [Inject] protected IDatabaseToolsService DatabaseToolsService { get; init; } = null!;

    [Inject] protected IFilePickerService FilePickerService { get; init; } = null!;

    protected string ImportDatabaseButtonText
    {
        get
        {
            ResetAutoImportStateIfPathChanged();

            return AutoImportStateLocalizer.Label(Localizer, _autoImportState);
        }
    }

    protected bool IsCancelling { get; set; }

    protected bool IsImportDatabaseButtonDisabled
    {
        get
        {
            ResetAutoImportStateIfPathChanged();

            return _autoImportState is AutoImportState.Importing or AutoImportState.Imported;
        }
    }

    // Post-success auto-import runs while IsRunning remains true; hide Cancel because it cannot cancel the import.
    protected bool IsImportInProgress => _autoImportState == AutoImportState.Importing;

    [Inject] protected IStringLocalizer<SharedResource> Localizer { get; init; } = null!;

    protected virtual string LogCategory => LogCategories.DatabaseTools;

    protected ImmutableList<LogRecord> LogEntries { get; set; } = ImmutableList<LogRecord>.Empty;

    [Inject] protected IOperationLogProgressFactory OperationLogProgressFactory { get; init; } = null!;

    protected DatabaseToolsResult? Outcome { get; set; }

    [Inject] protected IDatabaseToolsPickerDirectory PickerDirectory { get; init; } = null!;

    // Uses the dispatch-time snapshot so later target-field edits cannot change the import target.
    protected string? ProducedDatabasePath =>
        Outcome?.Outcome == DatabaseToolsOutcome.Succeeded ? _producedDatabasePathSnapshot : null;

    protected virtual string? ProducedDatabasePathCandidate => null;

    protected bool ShouldShowImportDatabaseButton =>
        Outcome?.Outcome == DatabaseToolsOutcome.Succeeded && ProducedDatabasePath is not null;

    private protected AutoImportMode AutoImportMode { get; set; } = AutoImportMode.Off;

    public void CancelIfRunning()
    {
        try { Cts?.Cancel(); }
        catch (ObjectDisposedException) { /* Already disposed; nothing to cancel. */ }
    }

    public virtual void Dispose()
    {
        _disposed = true;

        try { Cts?.Cancel(); }
        catch (ObjectDisposedException) { }

        Cts?.Dispose();

        lock (_pendingLock) { _pendingEntries.Clear(); }

        GC.SuppressFinalize(this);
    }

    // Buffers high-rate log entries into 50ms UI batches; late callbacks after disposal are dropped.
    protected void AppendEntry(LogRecord entry)
    {
        if (_disposed) { return; }

        bool needsSchedule;
        lock (_pendingLock)
        {
            _pendingEntries.Add(entry);
            needsSchedule = !_flushScheduled;
            if (needsSchedule) { _flushScheduled = true; }
        }

        if (!needsSchedule) { return; }

        // Task.Run keeps the delay off the renderer; InvokeAsync returns to it for the flush.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(FlushIntervalMs);

                if (_disposed) { return; }

                await InvokeAsync(FlushPendingEntriesCore);
            }
            catch (ObjectDisposedException) { /* Component disposed mid-flush; drop. */ }
        });
    }

    protected void AppendOutcome(DatabaseToolsResult result)
    {
        var level = result.Outcome switch
        {
            DatabaseToolsOutcome.Succeeded => LogLevel.Information,
            DatabaseToolsOutcome.Cancelled => LogLevel.Warning,
            DatabaseToolsOutcome.Failed => LogLevel.Error,
            _ => LogLevel.Information
        };

        string durationSeconds = result.Duration.TotalSeconds.ToString("F1");
        var message = result.Outcome switch
        {
            DatabaseToolsOutcome.Succeeded => Localizer["DatabaseTools_OutcomeMessage_Succeeded", durationSeconds],
            DatabaseToolsOutcome.Cancelled => Localizer["DatabaseTools_OutcomeMessage_Cancelled", durationSeconds],
            DatabaseToolsOutcome.Failed => string.IsNullOrWhiteSpace(result.FailureSummary) ?
                Localizer["DatabaseTools_OutcomeMessage_FailedSeeDebugLog"] :
                Localizer["DatabaseTools_OutcomeMessage_FailedWithSummary", result.FailureSummary],
            _ => string.Empty
        };

        AppendEntry(new LogRecord(DateTime.UtcNow, level, message));
    }

    protected abstract TRequest BuildRequest();

    protected void CancelRun()
    {
        if (!IsRunning) { return; }

        IsCancelling = true;

        try { Cts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    // The request is built only after confirmation so edits during the prompt are re-read.
    protected virtual Task<bool> ConfirmBeforeDispatchAsync() => Task.FromResult(true);

    protected abstract Task<DatabaseToolsResult> DispatchAsync(
        TRequest request,
        IProgress<LogRecord> logProgress,
        CancellationToken cancellationToken);

    protected async Task FlushPendingEntriesAsync()
    {
        if (_disposed) { return; }

        try { await InvokeAsync(FlushPendingEntriesCore); }
        catch
        {
            // Cleanup must not mask the original run outcome.
        }
    }

    protected Task ImportProducedDatabaseAsync() =>
        RequestProducedDatabaseImportAsync(AutoImportMode == AutoImportMode.ImportAndEnable);

    protected virtual async Task OnRunSucceededAsync(DatabaseToolsResult result, CancellationToken cancellationToken)
    {
        if (AutoImportMode == AutoImportMode.Off ||
            ProducedDatabasePath is not { } producedDatabasePath ||
            !File.Exists(producedDatabasePath))
        {
            return;
        }

        await RequestProducedDatabaseImportAsync(AutoImportMode == AutoImportMode.ImportAndEnable);
    }

    protected async Task<string?> PickFileAsync(
        string pickerTitle,
        IReadOnlyList<string> extensions,
        DatabaseToolsPickRole role)
    {
        string? initialDirectory = await PickerDirectory.ResolveInitialDirectoryAsync(role);
        string? path = await FilePickerService.PickAsync(pickerTitle, extensions, initialDirectory);
        PickerDirectory.RememberDirectory(role, path);

        return path;
    }

    protected async Task<string?> PickSaveFileAsync(
        string pickerTitle,
        IReadOnlyList<string> extensions,
        DatabaseToolsPickRole role,
        string? suggestedFileName = null)
    {
        string? initialDirectory = await PickerDirectory.ResolveInitialDirectoryAsync(role);
        string? path = await FilePickerService.PickSaveAsync(pickerTitle, extensions, suggestedFileName, initialDirectory);
        PickerDirectory.RememberDirectory(role, path);

        return path;
    }

    protected Task RunAsync() => RunCoreAsync((request, logProgress, ct) => DispatchAsync(request, logProgress, ct));

    protected Task RunElevatedAsync(Func<TRequest, IProgress<LogRecord>, CancellationToken, Task<DatabaseToolsResult>> elevatedDispatcher) =>
        RunCoreAsync(elevatedDispatcher);

    private void FlushPendingEntriesCore()
    {
        if (_disposed) { return; }

        LogRecord[] batch;

        lock (_pendingLock)
        {
            if (_pendingEntries.Count == 0)
            {
                _flushScheduled = false;

                return;
            }

            batch = [.. _pendingEntries];
            _pendingEntries.Clear();
            _flushScheduled = false;
        }

        LogEntries = LogEntries.AddRange(batch);

        StateHasChanged();
    }

    private async Task RequestProducedDatabaseImportAsync(bool enable)
    {
        ResetAutoImportStateIfPathChanged();

        if (ProducedDatabasePath is not { } producedDatabasePath) { return; }

        if (_autoImportState is AutoImportState.Importing or AutoImportState.Imported) { return; }
        
        if (RequestAutoImportAsync is null)
        {
            _autoImportState = AutoImportState.Failed;
            return;
        }

        _autoImportState = AutoImportState.Importing;
        _autoImportPath = producedDatabasePath;

        // Render Importing before awaiting the import, or the UI never shows that state.
        try { await InvokeAsync(StateHasChanged); }
        catch (ObjectDisposedException) { }

        try
        {
            await RequestAutoImportAsync(producedDatabasePath, enable);
            _autoImportState = AutoImportState.Imported;
        }
        catch (OperationCanceledException)
        {
            _autoImportState = AutoImportState.Failed;
        }
        catch (AutoImportIncompleteException)
        {
            AppendEntry(new LogRecord(
                DateTime.UtcNow,
                LogLevel.Warning,
                Localizer["DatabaseTools_Log_ImportFailed",
                Localizer["DatabaseTools_AutoImport_DidNotComplete"]]));
            _autoImportState = AutoImportState.Failed;
        }
        catch (Exception ex)
        {
            AppendEntry(new LogRecord(DateTime.UtcNow, LogLevel.Warning, Localizer["DatabaseTools_Log_ImportFailed", ex.Message]));
            _autoImportState = AutoImportState.Failed;
        }
    }

    private void ResetAutoImportStateIfPathChanged()
    {
        var producedDatabasePath = ProducedDatabasePath;

        if (string.Equals(_autoImportPath, producedDatabasePath, StringComparison.OrdinalIgnoreCase)) { return; }

        _autoImportPath = producedDatabasePath;
        _autoImportState = AutoImportState.NotImported;
    }

    private async Task RunCoreAsync(Func<TRequest, IProgress<LogRecord>, CancellationToken, Task<DatabaseToolsResult>> dispatcher)
    {
        if (IsRunning || IsConfirming || _autoImportState == AutoImportState.Importing || !CanRun) { return; }

        // Build the request after confirmation so prompt-time edits are honored and overlapping runs stay blocked.
        IsConfirming = true;

        try
        {
            if (!await ConfirmBeforeDispatchAsync()) { return; }
        }
        finally
        {
            IsConfirming = false;
        }

        var request = BuildRequest();

        LogEntries = ImmutableList<LogRecord>.Empty;
        Outcome = null;
        _autoImportPath = null;
        _autoImportState = AutoImportState.NotImported;
        _producedDatabasePathSnapshot = ProducedDatabasePathCandidate;
        IsRunning = true;
        IsCancelling = false;
        Cts = new CancellationTokenSource();

        lock (_pendingLock)
        {
            _pendingEntries.Clear();
            _flushScheduled = false;
        }

        IProgress<LogRecord> logProgress = OperationLogProgressFactory.Create(new Progress<LogRecord>(AppendEntry), LogCategory, VerboseLogging);
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            var result = await dispatcher(request, logProgress, Cts.Token);
            Outcome = result;
            AppendOutcome(result);
        }
        catch (Exception ex)
        {
            var failedOutcome = new DatabaseToolsResult(DatabaseToolsOutcome.Failed, ex.Message, Stopwatch.GetElapsedTime(startTimestamp));
            Outcome = failedOutcome;
            AppendEntry(new LogRecord(DateTime.UtcNow, LogLevel.Error, Localizer["DatabaseTools_Log_UnexpectedError", ex.Message]));
            AppendOutcome(failedOutcome);
        }
        finally
        {
            IsCancelling = false;
            var runCts = Cts;

            if (!_disposed)
            {
                await FlushPendingEntriesAsync();

                if (Outcome is { Outcome: DatabaseToolsOutcome.Succeeded } result)
                {
                    await OnRunSucceededAsync(result, runCts?.Token ?? CancellationToken.None);
                }
            }

            // Keep IsRunning true through auto-import so a second run cannot replace this CTS before teardown.
            IsRunning = false;

            // Only clear this run's CTS; a re-entrant run may already own the field.
            if (ReferenceEquals(Cts, runCts)) { Cts = null; }

            runCts?.Dispose();

            if (!_disposed)
            {
                try { await InvokeAsync(StateHasChanged); }
                catch (ObjectDisposedException) { }
            }
        }
    }

    private protected string FormatAutoImportMode(AutoImportMode mode) => AutoImportModeLocalizer.Label(Localizer, mode);
}
