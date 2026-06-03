// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Contracts;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.DatabaseTools;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Diagnostics;

namespace EventLogExpert.UI.DatabaseTools.Tabs;

/// <summary>
///     Shared base for the five DatabaseTools tab components. Captures the cross-tab plumbing — run/cancel/cancelling
///     state, log streaming, outcome handling, verbose-cascading-parameter, and the <c>RunAsync</c> dispatch shell — so
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

    private readonly List<DatabaseToolsLogEntry> _pendingEntries = [];
    private readonly Lock _pendingLock = new();
    private volatile bool _disposed;
    private bool _flushScheduled;

    public bool IsRunning { get; protected set; }

    [CascadingParameter(Name = "VerboseLogging")] public bool VerboseLogging { get; set; }

    /// <summary>
    ///     Default validation: always runnable. Concrete tabs override to require non-empty required fields and a
    ///     non-error filter state.
    /// </summary>
    protected virtual bool CanRun => true;

    protected CancellationTokenSource? Cts { get; set; }

    [Inject] protected IDatabaseToolsService DatabaseToolsService { get; init; } = null!;

    [Inject] protected IFilePickerService FilePickerService { get; init; } = null!;

    protected bool IsCancelling { get; set; }

    protected ImmutableList<DatabaseToolsLogEntry> LogEntries { get; set; } = ImmutableList<DatabaseToolsLogEntry>.Empty;

    protected DatabaseToolsResult? Outcome { get; set; }

    /// <summary>Cancels the in-flight operation if any. Safe to call on a torn-down tab.</summary>
    public void CancelIfRunning()
    {
        try { Cts?.Cancel(); }
        catch (ObjectDisposedException) { /* Already disposed — nothing to cancel. */ }
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
    ///     invoke after <see cref="Dispose" /> — late Progress-sink callbacks (e.g., the operation completing after the user
    ///     closes the modal) are dropped silently rather than throwing <see cref="ObjectDisposedException" />.
    /// </summary>
    protected void AppendEntry(DatabaseToolsLogEntry entry)
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

        AppendEntry(new DatabaseToolsLogEntry(DateTime.UtcNow, level, message));
    }

    /// <summary>Build the operation-specific request record from the tab's current field state.</summary>
    protected abstract TRequest BuildRequest();

    /// <summary>
    ///     Marks the active run as cancelling and signals the token. The visible button switches to "Cancelling…" until
    ///     the operation observes cancellation and the <c>finally</c> in <see cref="RunAsync" /> clears state.
    /// </summary>
    protected void CancelRun()
    {
        if (!IsRunning) { return; }

        IsCancelling = true;

        try { Cts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    /// <summary>Dispatch the operation against the configured <see cref="IDatabaseToolsService" />.</summary>
    protected abstract Task<DatabaseToolsResult> DispatchAsync(
        TRequest request,
        IProgress<DatabaseToolsLogEntry> logSink,
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

    /// <summary>
    ///     Convenience for tabs that have a Browse… button next to a path field. Returns the picked path (or <c>null</c>
    ///     if cancelled) without mutating state — the caller decides which field to update.
    /// </summary>
    protected Task<string?> PickFileAsync(string pickerTitle, IReadOnlyList<string> extensions) =>
        FilePickerService.PickAsync(pickerTitle, extensions);

    /// <summary>
    ///     Convenience for tabs whose Browse… button picks a SAVE destination (i.e. a path that doesn't have to exist
    ///     yet). The dialog prompts before overwriting existing files. Returns the picked path (or <c>null</c> if cancelled)
    ///     without mutating state — the caller decides which field to update.
    /// </summary>
    protected Task<string?> PickSaveFileAsync(string pickerTitle, IReadOnlyList<string> extensions, string? suggestedFileName = null) =>
        FilePickerService.PickSaveAsync(pickerTitle, extensions, suggestedFileName);

    /// <summary>
    ///     Standard 3-state Run shell. Resets log/outcome state, builds the request, dispatches to the service, and
    ///     translates the result into UI state. Wrapped exceptions surface as a synthetic Failed outcome with the exception
    ///     message rather than tearing down the component tree.
    /// </summary>
    protected Task RunAsync() => RunCoreAsync((request, logSink, ct) => DispatchAsync(request, logSink, ct));

    protected Task RunElevatedAsync(Func<TRequest, IProgress<DatabaseToolsLogEntry>, CancellationToken, Task<DatabaseToolsResult>> elevatedDispatcher) =>
        RunCoreAsync(elevatedDispatcher);

    /// <summary>
    ///     Drains the pending buffer and renders the accumulated batch. Must be called on the Blazor renderer thread
    ///     (i.e. via <see cref="ComponentBase.InvokeAsync(System.Action)" />).
    /// </summary>
    private void FlushPendingEntriesCore()
    {
        if (_disposed) { return; }

        DatabaseToolsLogEntry[] batch;

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

    private async Task RunCoreAsync(Func<TRequest, IProgress<DatabaseToolsLogEntry>, CancellationToken, Task<DatabaseToolsResult>> dispatcher)
    {
        if (IsRunning || !CanRun) { return; }

        var request = BuildRequest();

        LogEntries = ImmutableList<DatabaseToolsLogEntry>.Empty;
        Outcome = null;
        IsRunning = true;
        IsCancelling = false;
        Cts = new CancellationTokenSource();

        lock (_pendingLock)
        {
            _pendingEntries.Clear();
            _flushScheduled = false;
        }

        var logSink = new Progress<DatabaseToolsLogEntry>(AppendEntry);
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
            AppendEntry(new DatabaseToolsLogEntry(
                DateTime.UtcNow,
                LogLevel.Error,
                $"Unexpected error: {ex.Message}"));
            AppendOutcome(failedOutcome);
        }
        finally
        {
            IsRunning = false;
            IsCancelling = false;
            Cts?.Dispose();
            Cts = null;

            if (!_disposed)
            {
                await FlushPendingEntriesAsync();

                try { await InvokeAsync(StateHasChanged); }
                catch (ObjectDisposedException)
                {
                }
            }
        }
    }
}
