// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Contracts;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.DatabaseTools;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

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
        try { Cts?.Cancel(); }
        catch (ObjectDisposedException) { }

        Cts?.Dispose();

        GC.SuppressFinalize(this);
    }

    /// <summary>Thread-safe log-entry append. Always re-dispatches to the UI thread via <see cref="InvokeAsync" />.</summary>
    protected void AppendEntry(DatabaseToolsLogEntry entry)
    {
        _ = InvokeAsync(() =>
        {
            LogEntries = LogEntries.Add(entry);
            StateHasChanged();
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
    protected async Task RunAsync()
    {
        if (IsRunning || !CanRun) { return; }

        var request = BuildRequest();

        LogEntries = ImmutableList<DatabaseToolsLogEntry>.Empty;
        Outcome = null;
        IsRunning = true;
        IsCancelling = false;
        Cts = new CancellationTokenSource();

        var logSink = new Progress<DatabaseToolsLogEntry>(AppendEntry);

        try
        {
            var result = await DispatchAsync(request, logSink, Cts.Token);
            Outcome = result;
            AppendOutcome(result);
        }
        catch (Exception ex)
        {
            Outcome = new DatabaseToolsResult(DatabaseToolsOutcome.Failed, ex.Message, TimeSpan.Zero);
            AppendEntry(new DatabaseToolsLogEntry(
                DateTime.UtcNow,
                LogLevel.Error,
                $"Unexpected error: {ex.Message}"));
        }
        finally
        {
            IsRunning = false;
            IsCancelling = false;
            Cts?.Dispose();
            Cts = null;
            await InvokeAsync(StateHasChanged);
        }
    }
}
