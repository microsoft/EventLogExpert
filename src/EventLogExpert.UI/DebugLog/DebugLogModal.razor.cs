// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Common.Display;
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
    private readonly List<DebugLogFilterDraft> _filters = [];

    private Button? _addButton;
    private List<string> _displayed = [];
    private ReversedListView<string> _displayedView;
    private Guid? _editingFilterId;
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

    public DebugLogModal()
    {
        _displayedView = new ReversedListView<string>(_displayed);
    }

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    private bool CanAddFilter => _filters.TrueForAll(static filter => filter.IsComplete);

    [Inject] private IClipboardService ClipboardService { get; init; } = null!;

    [Inject] private IFileLogger FileLogger { get; init; } = null!;

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
        if (!CanAddFilter) { return; }

        var draft = new DebugLogFilterDraft();
        _filters.Add(draft);
        _editingFilterId = draft.Id;
        _focusEditorAfterRender = draft.Id;
        // A fresh draft is incomplete, so it is a projection no-op until the user configures a value.
    }

    private void ApplyIncrementalProjection()
    {
        if (_projectedThroughIndex >= _entries.Count) { return; }

        var (newLines, newCount) = DebugLogProjection.ProjectRange(
            _entries,
            _projectedThroughIndex,
            _entries.Count,
            SnapshotFilters());

        if (newLines.Count > 0) { _displayed.AddRange(newLines); }

        _filteredEntryCount += newCount;
        _projectedThroughIndex = _entries.Count;
    }

    private void ApplyProjection()
    {
        var (lines, count) = DebugLogProjection.Project(_entries, SnapshotFilters());

        SetDisplayed(lines);
        _filteredEntryCount = count;
        _projectedThroughIndex = _entries.Count;
    }

    private IReadOnlyList<string> CategoryFilterOptions()
    {
        var options = new SortedSet<string>(_availableCategories, StringComparer.Ordinal);

        if (_hasUncategorized) { options.Add(string.Empty); }

        return [.. options];
    }

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
        _availableCategories.Clear();
        _hasUncategorized = false;
        _hasLoaded = true;

        StateHasChanged();
    }

    private void CollapseEditor()
    {
        var previouslyEditing = _editingFilterId;
        _editingFilterId = null;
        _focusChipAfterRender = previouslyEditing;
    }

    private async Task HandleCopyAsync()
    {
        if (_filteredEntryCount == 0) { return; }

        await ClipboardService.CopyTextAsync(string.Join(Environment.NewLine, _displayedView));
    }

    private async Task HandleExportAsync()
    {
        if (_filteredEntryCount == 0) { return; }

        var suggestedFileName = $"debug-log-{DateTime.Now:yyyyMMdd-HHmmss}.log";
        var snapshot = _displayedView.ToArray();

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

    private void OnFilterChanged() => ApplyProjection();

    private void PruneStaleEditorRefs()
    {
        if (_editorRefs.Count == 0) { return; }

        if (_filters.Count == 0)
        {
            _editorRefs.Clear();
            return;
        }

        var liveIds = _filters.Select(static filter => filter.Id).ToHashSet();

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

        var parser = new DebugLogEntryStreamParser();
        var sinceRender = 0;
        Exception? loadException = null;

        try
        {
            await foreach (var line in FileLogger.LoadAsync(loadCts.Token))
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

    private void RemoveFilter(DebugLogFilterDraft draft)
    {
        if (_editingFilterId == draft.Id) { _editingFilterId = null; }

        _filters.Remove(draft);
        _focusAddButtonAfterRender = true;
        ApplyProjection();
    }

    private void SetDisplayed(List<string> lines)
    {
        _displayed = lines;
        _displayedView = new ReversedListView<string>(_displayed);
    }

    private IReadOnlyList<DebugLogFilter> SnapshotFilters() => [.. _filters.Select(static draft => draft.ToFilter())];

    private void StartEditing(Guid filterId)
    {
        _editingFilterId = filterId;
        _focusEditorAfterRender = filterId;
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
}
