// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.DebugLog;
using EventLogExpert.UI.Focus;
using EventLogExpert.UI.Inputs;
using EventLogExpert.UI.Modal;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.DebugLog;

public sealed partial class DebugLogModal : ModalBase<bool>
{
    private const int RenderBatchSize = 100;
    private const int RowHeightPx = 18;

    private readonly SortedSet<string> _availableCategories = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, DebugLogFilterRow?> _editorRefs = new();
    private readonly List<DebugLogFilterRowState> _rows = [];

    private Button? _addButton;
    private IReadOnlyList<DebugLogFilter> _appliedFilters = [];
    private List<string> _displayed = [];
    private IReadOnlyList<DebugLogEntry> _entries = [];
    private int _filteredEntryCount;
    private bool _focusAddButtonAfterRender;
    private Guid? _focusChipAfterRender;
    private Guid? _focusEditorAfterRender;
    private bool _hasLoaded;
    private bool _hasUncategorized;
    private CancellationTokenSource? _loadCts;
    private int _loadGeneration;
    private int _projectedThroughIndex;

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IClipboardService ClipboardService { get; init; } = null!;

    [Inject] private IDebugLogReader DebugLogReader { get; init; } = null!;

    [Inject] private IFileSaveService FileSaveService { get; init; } = null!;

    protected override async ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing)
        {
            _loadGeneration++;
            // Only signal cancel; the owning Refresh's using var disposes its CTS.
            _loadCts?.Cancel();
        }

        await base.DisposeAsyncCore(disposing);
    }

    // After the chip<->editor swap unmounts the previously focused control, move keyboard focus to the new
    // target (newly opened editor, then collapsed chip, then the Add button) so focus never falls to the body.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        PruneStaleEditorRefs();

        if (_focusEditorAfterRender is { } editingId
            && _editorRefs.TryGetValue(editingId, out var editor)
            && editor is not null)
        {
            _focusEditorAfterRender = null;
            await editor.FocusEditorFirstControlAsync();
        }
        else if (_focusChipAfterRender is { } chipId
            && _editorRefs.TryGetValue(chipId, out var chipRow)
            && chipRow is not null)
        {
            _focusChipAfterRender = null;
            await chipRow.FocusChipEditButtonAsync();
        }
        else if (_focusAddButtonAfterRender)
        {
            _focusAddButtonAfterRender = false;

            if (_addButton is { } button) { await ElementFocus.SafelyAsync(button.Element); }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override async Task OnInitializedAsync()
    {
        await Refresh();

        await base.OnInitializedAsync();
    }

    private void AddFilter()
    {
        var row = new DebugLogFilterRowState { Editing = new DebugLogFilterDraft() };
        _rows.Add(row);
        _focusEditorAfterRender = row.Id;
    }

    private void ApplyIncrementalProjection()
    {
        if (_projectedThroughIndex >= _entries.Count) { return; }

        var (newLines, newCount) = DebugLogProjection.ProjectRange(
            _entries,
            _projectedThroughIndex,
            _entries.Count,
            _appliedFilters);

        if (newLines.Count > 0) { _displayed.AddRange(newLines); }

        _filteredEntryCount += newCount;
        _projectedThroughIndex = _entries.Count;
    }

    // Rebuild the applied filter set from the rows and fully reproject. Called on every action that changes the
    // applied set (Save / enable / exclude toggle / remove / clear); streaming reads the cached _appliedFilters.
    private void ApplyProjection()
    {
        _appliedFilters = [.. _rows.Where(static row => row.Applied is not null).Select(static row => row.Applied!)];

        var (lines, count) = DebugLogProjection.Project(_entries, _appliedFilters);

        SetDisplayed(lines);
        _filteredEntryCount = count;
        _projectedThroughIndex = _entries.Count;
    }

    private void CancelEditing(DebugLogFilterRowState row)
    {
        if (row.Applied is null)
        {
            _rows.Remove(row);
            _focusAddButtonAfterRender = true;
        }
        else
        {
            row.Editing = null;
            _focusChipAfterRender = row.Id;
        }
    }

    private IReadOnlyList<string> CategoryFilterOptions()
    {
        var options = new SortedSet<string>(_availableCategories, StringComparer.Ordinal);

        if (_hasUncategorized) { options.Add(string.Empty); }

        return [.. options];
    }

    private void ClearFilters()
    {
        _rows.Clear();
        _focusAddButtonAfterRender = true;
        ApplyProjection();
    }

    // Clears the debug-log FILE (not the filters), then resets the streamed view.
    private async Task ClearLog()
    {
        try
        {
            await DebugLogReader.ClearAsync();
        }
        catch (Exception ex)
        {
            await AlertDialogService.ShowAlert("Clear Log Failed", ex.Message, "OK");

            return;
        }

        _loadGeneration++;
        _loadCts?.Cancel();
        _entries = [];
        SetDisplayed([]);
        _filteredEntryCount = 0;
        _projectedThroughIndex = 0;
        _availableCategories.Clear();
        _hasUncategorized = false;
        _hasLoaded = true;

        StateHasChanged();
    }

    private async Task HandleCopyAsync()
    {
        if (_filteredEntryCount == 0) { return; }

        await ClipboardService.CopyTextAsync(string.Join(Environment.NewLine, _displayed));
    }

    private async Task HandleExportAsync()
    {
        if (_filteredEntryCount == 0) { return; }

        var suggestedFileName = $"debug-log-{DateTime.Now:yyyyMMdd-HHmmss}.log";
        var snapshot = _displayed.ToArray();

        try
        {
            await FileSaveService.SaveStreamingAsync(suggestedFileName, FileSaveFileTypes.Log, async (stream, _) =>
            {
                await using var writer = new StreamWriter(stream, leaveOpen: true);

                for (var i = 0; i < snapshot.Length; i++)
                {
                    if (i > 0) { await writer.WriteAsync(Environment.NewLine); }

                    await writer.WriteAsync(snapshot[i]);
                }
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            await AlertDialogService.ShowAlert("Export Failed", ex.Message, "OK");
        }
    }

    // An editor mutation (field/operator/value/exclude on the draft copy): re-render only so the Save button's
    // enabled state tracks completeness. Nothing reaches the projection until the user clicks Save on the row.
    private void OnEditorChanged() => StateHasChanged();

    private void PruneStaleEditorRefs()
    {
        if (_editorRefs.Count == 0) { return; }

        if (_rows.Count == 0)
        {
            _editorRefs.Clear();
            return;
        }

        var liveIds = _rows.Select(static row => row.Id).ToHashSet();

        var stale = _editorRefs
            .Where(entry => !liveIds.Contains(entry.Key) || entry.Value is null)
            .Select(entry => entry.Key)
            .ToList();

        foreach (var id in stale) { _editorRefs.Remove(id); }
    }

    private async Task Refresh()
    {
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
        _availableCategories.Clear();
        _hasUncategorized = false;
        StateHasChanged();

        var parser = new DebugLogEntryReverseStreamParser();
        var sinceRender = 0;
        Exception? loadException = null;

        try
        {
            await foreach (var line in DebugLogReader.LoadAsync(loadCts.Token))
            {
                if (generation != _loadGeneration) { return; }

                var emitted = parser.AddLine(line);

                if (emitted is not null)
                {
                    streamingEntries.Add(emitted);
                    TrackEntryCategory(emitted);
                }

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

                if (final is not null)
                {
                    streamingEntries.Add(final);
                    TrackEntryCategory(final);
                }

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

    private void RemoveFilter(DebugLogFilterRowState row)
    {
        _rows.Remove(row);
        _focusAddButtonAfterRender = true;
        ApplyProjection();
    }

    private void SaveFilter(DebugLogFilterRowState row)
    {
        if (row.Editing is not { IsComplete: true } draft) { return; }

        row.Applied = draft.ToFilter();
        row.Editing = null;
        _focusChipAfterRender = row.Id;
        ApplyProjection();
    }

    private void SetDisplayed(List<string> lines)
    {
        _displayed = lines;
    }

    private void StartEditing(DebugLogFilterRowState row)
    {
        row.Editing = DebugLogFilterDraft.FromFilter(row.Applied!);
        _focusEditorAfterRender = row.Id;
    }

    // A committed chip's live enable/disable toggle: flip IsEnabled on the applied filter and reproject.
    private void ToggleEnable(DebugLogFilterRowState row)
    {
        if (row.Applied is not { } applied) { return; }

        row.Applied = applied with { IsEnabled = !applied.IsEnabled };
        ApplyProjection();
    }

    // A committed chip's live include/exclude toggle: flip IsExcluded on the applied filter and reproject.
    private void ToggleExclude(DebugLogFilterRowState row)
    {
        if (row.Applied is not { } applied) { return; }

        row.Applied = applied with { IsExcluded = !applied.IsExcluded };
        ApplyProjection();
    }

    private void TrackEntryCategory(DebugLogEntry entry)
    {
        if (entry.Category is { Length: > 0 } category)
        {
            _availableCategories.Add(category);
        }
        else
        {
            _hasUncategorized = true;
        }
    }

    // A single filter row: its committed, immutable Applied filter (shown as a chip; null until first Save) and an
    // optional Editing draft copy (shown as the editor while non-null). Editing never mutates Applied - that is the
    // edit-on-copy invariant that keeps the chip truthful.
    private sealed class DebugLogFilterRowState
    {
        public DebugLogFilter? Applied { get; set; }

        public DebugLogFilterDraft? Editing { get; set; }

        public Guid Id { get; } = Guid.NewGuid();
    }
}
