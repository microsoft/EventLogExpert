// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Common.Files;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System.Collections.Immutable;

namespace EventLogExpert.UI.DatabaseTools;

/// <summary>
///     Severity-colored, monospace log surface used by each DatabaseTools tab. Auto-scrolls to bottom on new entries
///     ONLY when the user is already at the bottom; pauses auto-scroll and surfaces a "Jump to latest" pill when the user
///     has scrolled up to inspect earlier output.
/// </summary>
public sealed partial class DatabaseToolsLogView : IAsyncDisposable
{
    private readonly DotNetObjectReference<DatabaseToolsLogView> _selfRef;

    private volatile bool _disposed;
    private bool _isAutoScrollPinned = true;
    private IJSObjectReference? _jsModule;
    private int _lastRenderedCount;
    private ElementReference _logViewRef;
    private bool _showJumpToLatest;

    public DatabaseToolsLogView() => _selfRef = DotNetObjectReference.Create(this);

    [Parameter] public ImmutableList<DatabaseToolsLogEntry> Entries { get; set; } = ImmutableList<DatabaseToolsLogEntry>.Empty;

    [Parameter] public string FileNamePrefix { get; set; } = "database-tools";

    /// <summary>
    ///     Final outcome of the most recent operation. When non-null, the log toolbar shows a colored chip with the
    ///     outcome between the entries count and Copy / Export.
    /// </summary>
    [Parameter] public DatabaseToolsResult? Outcome { get; set; }

    /// <summary>
    ///     Content slot rendered on the LEFT side of the log toolbar (before the entries count). Tabs use this for their
    ///     Run / Cancel button so the action lives in the same toolbar as Copy / Export.
    /// </summary>
    [Parameter] public RenderFragment? ToolbarLeadingContent { get; set; }

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IClipboardService ClipboardService { get; init; } = null!;

    [Inject] private IFileSaveService FileSaveService { get; init; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    private string OutcomeChipAriaLive => Outcome?.Outcome == DatabaseToolsOutcome.Failed ? "assertive" : "polite";

    private string OutcomeChipCss => Outcome?.Outcome switch
    {
        DatabaseToolsOutcome.Succeeded => "outcome-chip outcome-succeeded",
        DatabaseToolsOutcome.Cancelled => "outcome-chip outcome-cancelled",
        DatabaseToolsOutcome.Failed => "outcome-chip outcome-failed",
        _ => "outcome-chip"
    };

    private string OutcomeChipIcon => Outcome?.Outcome switch
    {
        DatabaseToolsOutcome.Succeeded => "bi bi-check-circle",
        DatabaseToolsOutcome.Cancelled => "bi bi-slash-circle",
        DatabaseToolsOutcome.Failed => "bi bi-x-circle",
        _ => "bi bi-info-circle"
    };

    private string OutcomeChipRole => Outcome?.Outcome == DatabaseToolsOutcome.Failed ? "alert" : "status";

    private string OutcomeChipText
    {
        get
        {
            if (Outcome is null) { return string.Empty; }

            var seconds = Outcome.Duration.TotalSeconds;

            return Outcome.Outcome switch
            {
                DatabaseToolsOutcome.Succeeded => $"Succeeded in {seconds:F1}s",
                DatabaseToolsOutcome.Cancelled => $"Cancelled after {seconds:F1}s",
                DatabaseToolsOutcome.Failed => $"Failed: {Outcome.FailureSummary ?? "see debug log"}",
                _ => string.Empty
            };
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;

        if (_jsModule is not null)
        {
            try
            {
                await _jsModule.InvokeVoidAsync("detach", _logViewRef);
            }
            catch (JSDisconnectedException) { /* Circuit gone — listener already implicitly detached. */ }
            catch (JSException) { /* detach() unavailable in legacy module — best-effort cleanup. */ }

            try
            {
                await _jsModule.DisposeAsync();
            }
            catch (JSDisconnectedException) { /* Circuit gone — nothing to clean. */ }
        }

        _selfRef.Dispose();
    }

    [JSInvokable]
    public void OnPinStateChanged(bool isPinned)
    {
        if (_disposed) { return; }

        if (_isAutoScrollPinned == isPinned && _showJumpToLatest == !isPinned) { return; }

        _isAutoScrollPinned = isPinned;
        _showJumpToLatest = !isPinned;

        try
        {
            _ = InvokeAsync(() =>
            {
                if (_disposed) { return; }
                StateHasChanged();
            });
        }
        catch (ObjectDisposedException)
        {
            // Disposed between _disposed check and dispatch; ignore.
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            if (_disposed) { return; }

            // Inline JS module: scroll + pin tracker. Loaded lazily.
            try
            {
                _jsModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
                    "import",
                    "./_content/EventLogExpert.UI/DatabaseTools/DatabaseToolsLogView.js");

                await _jsModule.InvokeVoidAsync("attach", _logViewRef, _selfRef);
                _lastRenderedCount = Entries.Count;
            }
            catch (JSDisconnectedException) { /* Closed mid-import — ignore. */ }
            catch (JSException) { /* Stale module/ref — best-effort attach. */ }

            return;
        }

        if (Entries.Count != _lastRenderedCount)
        {
            // Track shrink (new run resets Entries to empty) and grow uniformly so auto-scroll
            // re-arms on the next batch of entries after a reset.
            var grew = Entries.Count > _lastRenderedCount;
            _lastRenderedCount = Entries.Count;

            if (!grew)
            {
                // Shrink → new run reset. Re-arm pinned state so the next batch auto-scrolls
                // regardless of prior-run scroll position; JS pin tracker will re-compute on
                // the user's next scroll event. StateHasChanged because the render that just
                // committed drew the pill from the prior-run state.
                _isAutoScrollPinned = true;
                _showJumpToLatest = false;
                StateHasChanged();
            }
            else if (_isAutoScrollPinned && _jsModule is not null)
            {
                try
                {
                    await _jsModule.InvokeVoidAsync("scrollToBottom", _logViewRef);
                }
                catch (JSDisconnectedException) { /* Circuit gone — ignore. */ }
            }
        }
    }

    private static string FormatEntry(DatabaseToolsLogEntry entry) =>
        $"[{entry.TimestampUtc:HH:mm:ss.fff}] [{entry.Level}] {entry.Message}";

    private static string SeverityClass(LogLevel level) => level switch
    {
        LogLevel.Trace => "log-trace",
        LogLevel.Debug => "log-debug",
        LogLevel.Information => "log-info",
        LogLevel.Warning => "log-warning",
        LogLevel.Error => "log-error",
        LogLevel.Critical => "log-critical",
        _ => "log-info"
    };

    private async Task CopyAllAsync()
    {
        if (Entries.Count == 0) { return; }

        var text = string.Join(Environment.NewLine, Entries.Select(FormatEntry));

        await ClipboardService.CopyTextAsync(text);
    }

    private async Task ExportAsync()
    {
        if (Entries.Count == 0) { return; }

        var suggestedFileName = $"{FileNamePrefix}-{DateTime.Now:yyyyMMdd-HHmmss}.log";
        var snapshot = Entries.Select(FormatEntry).ToArray();

        try
        {
            await FileSaveService.SaveAsync(suggestedFileName, FileSaveFileTypes.Log, async stream =>
            {
                await using var writer = new StreamWriter(stream, leaveOpen: true);

                for (var i = 0; i < snapshot.Length; i++)
                {
                    if (i > 0) { await writer.WriteAsync(Environment.NewLine); }

                    await writer.WriteAsync(snapshot[i]);
                }
            });
        }
        catch (Exception ex)
        {
            await AlertDialogService.ShowAlert("Export Failed", ex.Message, "OK");
        }
    }

    private async Task JumpToLatest()
    {
        if (_jsModule is null) { return; }

        try
        {
            await _jsModule.InvokeVoidAsync("scrollToBottom", _logViewRef);
        }
        catch (JSDisconnectedException) { /* Circuit gone — ignore. */ }
    }
}
