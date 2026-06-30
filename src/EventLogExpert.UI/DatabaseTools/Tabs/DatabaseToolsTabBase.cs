// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.DatabaseTools.ShowProviders;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Sinks;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.DatabaseTools;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Diagnostics;

namespace EventLogExpert.UI.DatabaseTools.Tabs;

/// <summary>
///     Shared base for the five DatabaseTools tab components. Captures the cross-tab plumbing - run/cancel/cancelling
///     state, log streaming, outcome handling, verbose-cascading-parameter, and the <c>RunAsync</c> dispatch shell - so
///     each concrete tab only has to express the operation-specific request shape, validation, and service call.
/// </summary>
/// <typeparam name="TRequest">
///     The <c>Request</c> record consumed by the operation (e.g.
///     <see cref="ShowProvidersRequest" />). Each concrete tab binds this to the matching request type and uses
///     <see cref="BuildRequest" /> + <see cref="DispatchAsync" /> to wire it through.
/// </typeparam>
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

    // The produced .db path captured at request-build time, so a successful run's auto-import / "Import database" button
    // targets the file that was actually built - never a path the user edited in the (re-enabled) target field afterward.
    private string? _producedDatabasePathSnapshot;

    private enum AutoImportState
    {
        NotImported,
        Importing,
        Imported,
        Failed,
    }

    /// <summary>
    ///     True while <see cref="ConfirmBeforeDispatchAsync" /> is awaiting the user's decision (e.g. an overwrite
    ///     confirmation). Concrete tabs fold this into their disabled state so a second Run click during the prompt is a no-op
    ///     rather than appearing broken.
    /// </summary>
    public bool IsConfirming { get; private set; }

    public bool IsRunning { get; protected set; }

    [CascadingParameter(Name = "RequestAutoImport")] public Func<string, bool, Task>? RequestAutoImportAsync { get; set; }

    [CascadingParameter(Name = "VerboseLogging")] public bool VerboseLogging { get; set; }

    /// <summary>
    ///     Default validation: always runnable. Concrete tabs override to require non-empty required fields and a
    ///     non-error filter state.
    /// </summary>
    protected virtual bool CanRun => true;

    protected CancellationTokenSource? Cts { get; set; }

    [Inject] protected IDatabaseToolsService DatabaseToolsService { get; init; } = null!;

    [Inject] protected ITraceLogger FileLogger { get; init; } = null!;

    [Inject] protected IFilePickerService FilePickerService { get; init; } = null!;

    protected string ImportDatabaseButtonText
    {
        get
        {
            ResetAutoImportStateIfPathChanged();

            return _autoImportState switch
            {
                AutoImportState.Importing => "Importing database…",
                AutoImportState.Imported => "Database imported",
                AutoImportState.Failed => "Retry import database",
                _ => "Import database",
            };
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

    // True while a produced-database import is actually running. The post-success auto-import runs INSIDE the run's
    // finally with IsRunning still true (so the whole operation stays non-re-entrant), which would otherwise leave the
    // leading toolbar showing a "Cancel" that only cancels the already-finished run, never the import. The leading
    // slot gates its Cancel affordance on this so it never presents a control that cannot do what it says.
    protected bool IsImportInProgress => _autoImportState == AutoImportState.Importing;

    protected ImmutableList<LogRecord> LogEntries { get; set; } = ImmutableList<LogRecord>.Empty;

    protected DatabaseToolsResult? Outcome { get; set; }

    // The importable database a successful run produced (Create/Merge target; null for Diff/Show). Returns the path
    // SNAPSHOTTED when the run was dispatched, gated on success, so it can never reflect a later edit of the target field.
    protected string? ProducedDatabasePath =>
        Outcome?.Outcome == DatabaseToolsOutcome.Succeeded ? _producedDatabasePathSnapshot : null;

    // The path this tab WOULD produce if the current run succeeds, read from the live input. Snapshotted at dispatch time
    // into the produced path; concrete tabs that produce an importable database (Create, Merge) override this.
    protected virtual string? ProducedDatabasePathCandidate => null;

    protected bool ShouldShowImportDatabaseButton =>
        Outcome?.Outcome == DatabaseToolsOutcome.Succeeded && ProducedDatabasePath is not null;

    private protected AutoImportMode AutoImportMode { get; set; } = AutoImportMode.Off;

    /// <summary>Cancels the in-flight operation if any. Safe to call on a torn-down tab.</summary>
    public void CancelIfRunning()
    {
        try { Cts?.Cancel(); }
        catch (ObjectDisposedException) { /* Already disposed - nothing to cancel. */ }
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

    /// <summary>
    ///     Thread-safe log-entry append. Buffers entries on a 50ms flush window so a high-rate operation
    ///     (Create/Merge/Diff can emit thousands of entries) renders in one batch per window instead of per entry. Safe to
    ///     invoke after <see cref="Dispose" /> - late Progress-sink callbacks (e.g., the operation completing after the user
    ///     closes the modal) are dropped silently rather than throwing <see cref="ObjectDisposedException" />.
    /// </summary>
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

        // Task.Run forces a threadpool hop so the await Task.Delay continuation runs on a stable SC.
        // The inner await InvokeAsync then explicitly dispatches to the Blazor renderer for the flush.
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

    /// <summary>
    ///     Synthesises a final summary log entry whose severity reflects the operation outcome (info / warning / error).
    ///     The text mirrors the same content that drives the <see cref="DatabaseToolsLogView" />'s outcome chip so users see
    ///     the same outcome in two places.
    /// </summary>
    protected void AppendOutcome(DatabaseToolsResult result)
    {
        var level = result.Outcome switch
        {
            DatabaseToolsOutcome.Succeeded => LogLevel.Information,
            DatabaseToolsOutcome.Cancelled => LogLevel.Warning,
            DatabaseToolsOutcome.Failed => LogLevel.Error,
            _ => LogLevel.Information
        };

        var message = result.Outcome switch
        {
            DatabaseToolsOutcome.Succeeded => $"Completed in {result.Duration.TotalSeconds:F1}s.",
            DatabaseToolsOutcome.Cancelled => $"[Cancelled after {result.Duration.TotalSeconds:F1}s]",
            DatabaseToolsOutcome.Failed => string.IsNullOrWhiteSpace(result.FailureSummary)
                ? "[Failed: see debug log]"
                : $"[Failed: {result.FailureSummary}]",
            _ => string.Empty
        };

        AppendEntry(new LogRecord(DateTime.UtcNow, level, message));
    }

    /// <summary>Build the operation-specific request record from the tab's current field state.</summary>
    protected abstract TRequest BuildRequest();

    /// <summary>
    ///     Marks the active run as cancelling and signals the token. The visible button switches to "Cancelling..." until
    ///     the operation observes cancellation and the <c>finally</c> in <see cref="RunAsync" /> clears state.
    /// </summary>
    protected void CancelRun()
    {
        if (!IsRunning) { return; }

        IsCancelling = true;

        try { Cts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    /// <summary>
    ///     Optional pre-dispatch confirmation gate. Runs after <see cref="CanRun" /> passes but BEFORE the request is
    ///     built and the operation starts, so an override can prompt the user (e.g. "overwrite the existing database?") and
    ///     abort the run by returning <see langword="false" />. The default permits the run. The base guards against
    ///     overlapping invocations via <see cref="IsConfirming" />; the request is built only after this returns true so a
    ///     target changed during the prompt is re-read.
    /// </summary>
    protected virtual Task<bool> ConfirmBeforeDispatchAsync() => Task.FromResult(true);

    /// <summary>Dispatch the operation against the configured <see cref="IDatabaseToolsService" />.</summary>
    protected abstract Task<DatabaseToolsResult> DispatchAsync(
        TRequest request,
        IProgress<LogRecord> logSink,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Forces an immediate flush of buffered entries. Used by <see cref="RunAsync" />'s finally arm so the user sees
    ///     all entries (including <see cref="AppendOutcome" />'s outcome line) the moment the run completes.
    /// </summary>
    protected async Task FlushPendingEntriesAsync()
    {
        if (_disposed) { return; }

        try { await InvokeAsync(FlushPendingEntriesCore); }
        catch
        {
            // Terminal cleanup path (RunAsync.finally); swallow all so the original outcome propagates.
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

    /// <summary>
    ///     Convenience for tabs that have a Browse... button next to a path field. Returns the picked path (or
    ///     <c>null</c> if cancelled) without mutating state - the caller decides which field to update.
    /// </summary>
    protected Task<string?> PickFileAsync(string pickerTitle, IReadOnlyList<string> extensions) =>
        FilePickerService.PickAsync(pickerTitle, extensions);

    /// <summary>
    ///     Convenience for tabs whose Browse... button picks a SAVE destination (i.e. a path that doesn't have to exist
    ///     yet). The dialog prompts before overwriting existing files. Returns the picked path (or <c>null</c> if cancelled)
    ///     without mutating state - the caller decides which field to update.
    /// </summary>
    protected Task<string?> PickSaveFileAsync(string pickerTitle, IReadOnlyList<string> extensions, string? suggestedFileName = null) =>
        FilePickerService.PickSaveAsync(pickerTitle, extensions, suggestedFileName);

    /// <summary>
    ///     Standard 3-state Run shell. Resets log/outcome state, builds the request, dispatches to the service, and
    ///     translates the result into UI state. Wrapped exceptions surface as a synthetic Failed outcome with the exception
    ///     message rather than tearing down the component tree.
    /// </summary>
    protected Task RunAsync() => RunCoreAsync((request, logSink, ct) => DispatchAsync(request, logSink, ct));

    protected Task RunElevatedAsync(Func<TRequest, IProgress<LogRecord>, CancellationToken, Task<DatabaseToolsResult>> elevatedDispatcher) =>
        RunCoreAsync(elevatedDispatcher);

    /// <summary>
    ///     Drains the pending buffer and renders the accumulated batch. Must be called on the Blazor renderer thread
    ///     (i.e. via <see cref="ComponentBase.InvokeAsync(System.Action)" />).
    /// </summary>
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

            batch = _pendingEntries.ToArray();
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

        // Render the Importing state (button text/disabled) before the awaited import; otherwise the UI never shows it.
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
        catch (Exception ex)
        {
            AppendEntry(new LogRecord(DateTime.UtcNow, LogLevel.Warning, $"Import failed: {ex.Message}"));
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

        // Pre-dispatch confirmation gate (e.g. overwrite prompt). Runs before the request is built so a target changed
        // while the prompt was open is re-read by BuildRequest, and the dispatch is skipped entirely if the user
        // declines. IsConfirming blocks overlapping invocations and lets the tab disable its controls during the prompt.
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

        IProgress<LogRecord> logSink = new FileTeeLogSink(new Progress<LogRecord>(AppendEntry), FileLogger);
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            var result = await dispatcher(request, logSink, Cts.Token);
            Outcome = result;
            AppendOutcome(result);
        }
        catch (Exception ex)
        {
            var failedOutcome = new DatabaseToolsResult(DatabaseToolsOutcome.Failed, ex.Message, Stopwatch.GetElapsedTime(startTimestamp));
            Outcome = failedOutcome;
            AppendEntry(new LogRecord(DateTime.UtcNow, LogLevel.Error, $"Unexpected error: {ex.Message}"));
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

            // Clear running state and tear down this run's CTS only AFTER any post-success auto-import has finished.
            // Keeping IsRunning true across OnRunSucceededAsync makes the tab non-re-entrant for the whole operation, so a
            // second Run cannot swap in a new CTS that this teardown would then null (silently breaking that run's cancel).
            IsRunning = false;

            // Only null the field if it still points at THIS run's CTS, so a re-entrant run's live CTS is never lost.
            if (ReferenceEquals(Cts, runCts)) { Cts = null; }

            runCts?.Dispose();

            if (!_disposed)
            {
                try { await InvokeAsync(StateHasChanged); }
                catch (ObjectDisposedException) { }
            }
        }
    }

    // Self-describing label for the auto-import ValueSelect, shared by Create and Merge. The closed box shows the chosen
    // post-run action (matching each item's text) so the collapsed control reads unambiguously.
    private protected static string FormatAutoImportMode(AutoImportMode mode) => mode switch
    {
        AutoImportMode.ImportAndEnable => "Import and enable",
        AutoImportMode.Import => "Import database",
        _ => "Don't import",
    };
}
