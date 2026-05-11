// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Components.Base;
using EventLogExpert.UI;
using EventLogExpert.UI.Common.Clipboard;
using EventLogExpert.UI.Common.Files;
using EventLogExpert.UI.DebugLog;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Components.Modals;

public sealed partial class DebugLogModal : ModalBase<bool>
{
    private const int RenderBatchSize = 100;
    private const int RowHeightPx = 18;
    private const int StringFilterDebounceMs = 250;

    private static readonly LogLevel[] s_logLevels =
    [
        LogLevel.Trace,
        LogLevel.Debug,
        LogLevel.Information,
        LogLevel.Warning,
        LogLevel.Error,
        LogLevel.Critical,
    ];

    private string _activeStringFilter = string.Empty;
    private List<string> _displayed = [];
    private ReversedListView<string> _displayedView;
    private IReadOnlyList<DebugLogEntry> _entries = [];
    private int _filteredEntryCount;
    private bool _hasLoaded;
    private FilterEvaluator _levelOperator = FilterEvaluator.Equals;
    private CancellationTokenSource? _loadCts;
    private int _loadGeneration;
    private List<LogLevel> _multiLevels = [];
    private string _pendingStringFilter = string.Empty;
    private int _projectedThroughIndex;
    private LogLevel? _singleLevel;
    private CancellationTokenSource? _stringFilterCts;
    private int _stringFilterGeneration;

    public DebugLogModal()
    {
        _displayedView = new ReversedListView<string>(_displayed);
    }

    [Inject] private IAlertDialogService AlertDialogService { get; set; } = null!;

    [Inject] private IClipboardService ClipboardService { get; set; } = null!;

    [Inject] private IFileLogger FileLogger { get; set; } = null!;

    [Inject] private IFileSaveService FileSaveService { get; set; } = null!;

    protected override async ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing)
        {
            _loadGeneration++;
            // Only signal cancel; the owning Refresh's using var disposes its CTS.
            _loadCts?.Cancel();
            _stringFilterCts?.Cancel();
            _stringFilterCts?.Dispose();
            _stringFilterCts = null;
        }

        await base.DisposeAsyncCore(disposing);
    }

    protected override async Task OnInitializedAsync()
    {
        await Refresh();

        await base.OnInitializedAsync();
    }

    private void ApplyIncrementalProjection()
    {
        if (_projectedThroughIndex >= _entries.Count) { return; }

        var levels = BuildLevelsForProjection();

        var (newLines, newCount) = DebugLogProjection.ProjectRange(
            _entries,
            _projectedThroughIndex,
            _entries.Count,
            _levelOperator,
            levels,
            _activeStringFilter);

        if (newLines.Count > 0) { _displayed.AddRange(newLines); }

        _filteredEntryCount += newCount;
        _projectedThroughIndex = _entries.Count;
    }

    private void ApplyProjection()
    {
        var levels = BuildLevelsForProjection();
        var (lines, count) = DebugLogProjection.Project(_entries, _levelOperator, levels, _activeStringFilter);

        SetDisplayed(lines);
        _filteredEntryCount = count;
        _projectedThroughIndex = _entries.Count;
    }

    private IReadOnlyList<LogLevel> BuildLevelsForProjection() =>
        _levelOperator == FilterEvaluator.MultiSelect
            ? _multiLevels
            : _singleLevel.HasValue ? [_singleLevel.Value] : [];

    private async Task Clear()
    {
        try
        {
            await FileLogger.ClearAsync();
        }
        catch (Exception ex)
        {
            await AlertDialogService.ShowAlert("Clear Failed", ex.Message, "OK");

            return;
        }

        _loadGeneration++;
        _loadCts?.Cancel();
        _entries = [];
        SetDisplayed([]);
        _filteredEntryCount = 0;
        _projectedThroughIndex = 0;
        _hasLoaded = true;

        StateHasChanged();
    }

    private void FlushPendingStringFilter()
    {
        if (SyncPendingStringFilter())
        {
            ApplyProjection();
        }
    }

    private async Task HandleCopyAsync()
    {
        FlushPendingStringFilter();

        if (_filteredEntryCount == 0) { return; }

        await ClipboardService.CopyTextAsync(string.Join(Environment.NewLine, _displayedView));
    }

    private async Task HandleExportAsync()
    {
        FlushPendingStringFilter();

        if (_filteredEntryCount == 0) { return; }

        var suggestedFileName = $"debug-log-{DateTime.Now:yyyyMMdd-HHmmss}.log";
        var snapshot = _displayedView.ToArray();

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

    private void HandleLevelOperatorChanged(FilterEvaluator op)
    {
        _levelOperator = op;
        SyncPendingStringFilter();
        ApplyProjection();
    }

    private void HandleMultiLevelsChanged(List<LogLevel> levels)
    {
        _multiLevels = levels;
        SyncPendingStringFilter();
        ApplyProjection();
    }

    private void HandleSingleLevelChanged(LogLevel? level)
    {
        _singleLevel = level;
        SyncPendingStringFilter();
        ApplyProjection();
    }

    private async Task HandleStringFilterInput(ChangeEventArgs args)
    {
        _pendingStringFilter = args.Value?.ToString() ?? string.Empty;

        var generation = ++_stringFilterGeneration;

        _stringFilterCts?.Cancel();
        _stringFilterCts?.Dispose();

        var cts = new CancellationTokenSource();
        _stringFilterCts = cts;

        try
        {
            await Task.Delay(StringFilterDebounceMs, cts.Token);

            if (generation != _stringFilterGeneration) { return; }

            _activeStringFilter = _pendingStringFilter;
            ApplyProjection();
            StateHasChanged();
        }
        catch (TaskCanceledException) { }
    }

    private async Task Refresh()
    {
        SyncPendingStringFilter();

        var generation = ++_loadGeneration;

        // Cancel the prior load; its own using var will dispose its CTS.
        _loadCts?.Cancel();
        using var loadCts = new CancellationTokenSource();
        _loadCts = loadCts;

        _hasLoaded = false;
        var streamingEntries = new List<DebugLogEntry>();
        _entries = streamingEntries;
        SetDisplayed([]);
        _filteredEntryCount = 0;
        _projectedThroughIndex = 0;
        StateHasChanged();

        var parser = new DebugLogEntryStreamParser();
        var sinceRender = 0;
        Exception? loadException = null;

        try
        {
            await foreach (var line in FileLogger.LoadAsync(loadCts.Token))
            {
                if (generation != _loadGeneration) { return; }

                var emitted = parser.AddLine(line);

                if (emitted is not null) { streamingEntries.Add(emitted); }

                if (++sinceRender < RenderBatchSize) { continue; }

                sinceRender = 0;

                ApplyIncrementalProjection();
                StateHasChanged();
                await Task.Yield();
            }
        }
        catch (OperationCanceledException) when (loadCts.IsCancellationRequested || generation != _loadGeneration) { }
        catch (Exception ex)
        {
            loadException = ex;
        }
        finally
        {
            if (generation == _loadGeneration)
            {
                var final = parser.Flush();

                if (final is not null) { streamingEntries.Add(final); }

                ApplyIncrementalProjection();
                _hasLoaded = true;

                StateHasChanged();
            }

            if (ReferenceEquals(_loadCts, loadCts)) { _loadCts = null; }
        }

        if (loadException is not null && generation == _loadGeneration)
        {
            await AlertDialogService.ShowAlert("Refresh Failed", loadException.Message, "OK");
        }
    }

    private void SetDisplayed(List<string> lines)
    {
        _displayed = lines;
        _displayedView = new ReversedListView<string>(_displayed);
    }

    private bool SyncPendingStringFilter()
    {
        if (_activeStringFilter == _pendingStringFilter) { return false; }

        _stringFilterCts?.Cancel();
        _stringFilterCts?.Dispose();
        _stringFilterCts = null;
        _stringFilterGeneration++;

        _activeStringFilter = _pendingStringFilter;

        return true;
    }
}
