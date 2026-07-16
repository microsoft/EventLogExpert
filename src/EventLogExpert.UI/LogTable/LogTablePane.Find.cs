// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.UI.LogTable.Find;
using EventLogExpert.UI.LogTable.Grouping;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EventLogExpert.UI.LogTable;

// In-view incremental Find (Ctrl+F): read-only over the current view; it never dispatches filter/lens/sort/selection actions.
public sealed partial class LogTablePane
{
    private const int FindChunkSize = 4096;
    private const int FindDebounceMs = 200;
    private const int MaxMarksPerCell = 32;

    private readonly HashSet<string> _findExpandedGroupKeys = [];

    private bool _findCaseSensitive;
    private int _findCurrentIndex = -1;
    private ValueKey? _findCurrentKey;
    private EventLocator? _findCurrentLocator;
    private CancellationTokenSource? _findDebounceCts;
    private int _findFocusSignal;
    private (EventLogId? TableId, ColumnName? GroupBy) _findGroupContext;
    private List<EventLocator> _findMatches = [];
    private HashSet<EventLocator> _findMatchSet = [];
    private bool _findOpen;
    private string _findQuery = string.Empty;
    private IDisposable? _findRegistration;
    private bool _findRenderRequested;
    private CancellationTokenSource? _findScanCts;
    private int _findScanEpoch;
    private bool _findScanning;
    private bool _findScrollToCurrentOnRender;
    private bool _findWholeWord;
    private string _findWrapAnnouncement = string.Empty;

    [Inject]
    private IFindCoordinator FindCoordinator { get; init; } = null!;

    private int FindCurrentOrdinal => _findCurrentIndex >= 0 ? _findCurrentIndex + 1 : 0;

    [Inject]
    private IFindMarkerSource FindMarkerSource { get; init; } = null!;

    private int FindMatchCount => _findMatches.Count;

    // Renders Blazor-escaped segments (never MarkupString) so event text can't inject markup; capped at MaxMarksPerCell so a pathological cell can't explode the DOM.
    private IReadOnlyList<FindSegment> BuildFindSegments(string? text)
    {
        string value = text ?? string.Empty;

        if (_findQuery.Length == 0 || value.Length == 0) { return [new FindSegment(value, IsMark: false)]; }

        var segments = new List<FindSegment>();
        StringComparison comparison = EventFindMatcher.ComparisonFor(_findCaseSensitive);
        int position = 0;
        int marks = 0;

        while (position < value.Length && marks < MaxMarksPerCell)
        {
            int hit = EventFindMatcher.IndexOfMatch(value, _findQuery, position, comparison, _findWholeWord);

            if (hit < 0) { break; }

            if (hit > position) { segments.Add(new FindSegment(value[position..hit], IsMark: false)); }

            segments.Add(new FindSegment(value.Substring(hit, _findQuery.Length), IsMark: true));
            position = hit + _findQuery.Length;
            marks++;
        }

        if (position < value.Length) { segments.Add(new FindSegment(value[position..], IsMark: false)); }

        return segments;
    }

    private void CancelFindScans()
    {
        // Bump the epoch so a scan already past its cancellation check is rejected at publish time.
        _findScanEpoch++;
        _findScanCts?.Cancel();
        _findScanCts?.Dispose();
        _findScanCts = null;
        _findScanning = false;
    }

    private void ClearFindMatches()
    {
        _findMatches = [];
        _findMatchSet = [];
        _findCurrentIndex = -1;
        _findCurrentKey = null;
        _findCurrentLocator = null;
        _findScanning = false;
        _findWrapAnnouncement = string.Empty;

        FindMarkerSource.Clear();
    }

    private Task CloseFind()
    {
        _findOpen = false;
        CancelFindScans();
        _findDebounceCts?.Cancel();
        _findDebounceCts?.Dispose();
        _findDebounceCts = null;

        // Re-collapse only the groups Find expanded, keeping the current match's group open so the cursor lands on the event.
        RecollapseFindGroups();

        if (TryGetCurrentMatchLocator(out EventLocator locator))
        {
            SetCursorEvent(locator);
        }

        _focusActiveOnNextRender = true;

        ClearFindMatches();
        _findQuery = string.Empty;
        RequestFindRender();

        return Task.CompletedTask;
    }

    private (EventLogId? TableId, ColumnName? GroupBy) CurrentFindGroupContext() =>
        (_currentTable?.Id, _logTableState.GroupBy);

    private async Task DebounceThenScanAsync(CancellationTokenSource cts)
    {
        try { await Task.Delay(FindDebounceMs, cts.Token); }
        catch (OperationCanceledException) { return; }

        if (!ReferenceEquals(_findDebounceCts, cts)) { return; }

        _findDebounceCts = null;
        cts.Dispose();

        StartFindScan();
    }

    private void DisposeFind()
    {
        _findRegistration?.Dispose();
        _findRegistration = null;

        CancelFindScans();

        _findDebounceCts?.Cancel();
        _findDebounceCts?.Dispose();
        _findDebounceCts = null;

        FindMarkerSource.Clear();
    }

    private int FindAnchorIndex()
    {
        // Anchor in event-display-index space (Rank / group StartIndex), not visible-row space, so the first jump targets the right match under headers / collapsed groups.
        if (_cursor is { Kind: TableRowKind.Event, Handle: { } handle })
        {
            int rank = _activeDisplayedEvents.Rank(handle);

            return rank >= 0 ? rank : 0;
        }

        if (_cursor is { Kind: TableRowKind.Header, GroupKey: { } key } &&
            _rowView is { } view &&
            view.TryGetGroupByKey(key, out EventGroup group))
        {
            return group.StartIndex;
        }

        return 0;
    }

    private void FindNext() => StepFind(1);

    private void FindPrevious() => StepFind(-1);

    private int FirstMatchAtOrAfterAnchor()
    {
        if (_findMatches.Count == 0) { return -1; }

        int anchor = FindAnchorIndex();

        for (int i = 0; i < _findMatches.Count; i++)
        {
            if (_activeDisplayedEvents.Rank(_findMatches[i]) >= anchor) { return i; }
        }

        return 0;
    }

    // Enter/F3 must act on the just-typed query, not a debounce-stale result set: flush the pending debounce and scan now.
    private void FlushPendingFindScan()
    {
        if (_findDebounceCts is null) { return; }

        _findDebounceCts.Cancel();
        _findDebounceCts.Dispose();
        _findDebounceCts = null;

        StartFindScan();
    }

    private string? GetFindState(DisplayRow row)
    {
        if (!_findOpen || _findMatchSet.Count == 0) { return null; }

        if (IsCurrentFindMatch(row)) { return "current"; }

        return _findMatchSet.Contains(row.Loc) ? "match" : null;
    }

    private bool IsCurrentFindMatch(DisplayRow row) =>
        _findOpen && _findCurrentLocator is { } locator && locator.Equals(row.Loc);

    // Called when the searchable text or event set changed; collapse/regroup deliberately do NOT call this (they keep the same view reference and Find keeps its group-expansion ownership).
    private void NotifyFindViewChanged()
    {
        if (!_findOpen) { return; }

        if (_findQuery.Length == 0)
        {
            CancelFindScans();
            ClearFindMatches();

            return;
        }

        StartFindScan();
    }

    private void OnFindCaseChanged(bool caseSensitive)
    {
        _findCaseSensitive = caseSensitive;
        ScheduleFindScan();
        RequestFindRender();
    }

    private void OnFindQueryChanged(string query)
    {
        _findQuery = query;
        ScheduleFindScan();
        RequestFindRender();
    }

    private void OnFindWholeWordChanged(bool wholeWord)
    {
        _findWholeWord = wholeWord;
        ScheduleFindScan();
        RequestFindRender();
    }

    private void OpenFind()
    {
        bool wasOpen = _findOpen;
        _findOpen = true;
        _findFocusSignal++;

        if (!wasOpen && _findQuery.Length > 0) { StartFindScan(); }

        RequestFindRender();
    }

    private void PruneFindGroupOwnershipOnContextChange()
    {
        if (_findExpandedGroupKeys.Count > 0 && !CurrentFindGroupContext().Equals(_findGroupContext))
        {
            _findExpandedGroupKeys.Clear();
        }
    }

    // Hand the current match timestamps (owner-tagged, ascending) to the timeline; collected free during the scan, the histogram re-bins them against its current window.
    private void PublishFindMarks(List<long> matchTicks)
    {
        if (_currentTable is not { } table)
        {
            FindMarkerSource.Clear();

            return;
        }

        long[] sorted = [.. matchTicks];
        Array.Sort(sorted);

        FindMarkerSource.Publish(table.Id, sorted);
    }

    private void PublishFindMatches(List<EventLocator> matches, List<long> matchTicks)
    {
        EventLocator? priorLocator = _findCurrentLocator;

        _findMatches = matches;
        _findMatchSet = new HashSet<EventLocator>(matches);
        _findScanning = false;
        _findWrapAnnouncement = string.Empty;

        ResolveCurrentMatchAfterScan(priorLocator);
        _findScrollToCurrentOnRender = _findCurrentIndex >= 0;

        PublishFindMarks(matchTicks);
        RequestFindRender();
    }

    private void RecollapseFindGroups()
    {
        if (_findExpandedGroupKeys.Count == 0) { return; }

        string? currentGroupKey = null;

        if (TryGetCurrentMatchLocator(out EventLocator locator) && _rowView is { } view)
        {
            int index = RowIndexOf(locator);

            if (index >= 0) { currentGroupKey = view.GroupForEvent(index).Key; }
        }

        foreach (string key in _findExpandedGroupKeys)
        {
            if (key == currentGroupKey) { continue; }

            SetGroupCollapsed(key, collapse: true);
        }

        _findExpandedGroupKeys.Clear();
    }

    private bool RecollapseSteppedAwayGroups(GroupedRowView view, string targetGroupKey)
    {
        bool collapsedAny = false;

        foreach (string key in _findExpandedGroupKeys.ToArray())
        {
            if (key == targetGroupKey) { continue; }

            if (view.TryGetGroupByKey(key, out EventGroup owned) && !owned.IsCollapsed)
            {
                _findExpandedGroupKeys.Remove(key);
                SetGroupCollapsed(key, collapse: true);
                collapsedAny = true;
            }
            else
            {
                _findExpandedGroupKeys.Remove(key);
            }
        }

        return collapsedAny;
    }

    private void RegisterFind() => _findRegistration = FindCoordinator.SetActivePane(OpenFind);

    private void RelinquishFindGroupOwnership() => _ = InvokeAsync(() => _findExpandedGroupKeys.Clear());

    private void RequestFindRender()
    {
        _findRenderRequested = true;
        StateHasChanged();
    }

    private void ResolveCurrentMatchAfterScan(EventLocator? priorLocator)
    {
        if (_findMatches.Count == 0)
        {
            _findCurrentIndex = -1;
            _findCurrentKey = null;
            _findCurrentLocator = null;

            return;
        }

        // Preserve the current match across a rescan: the locator survives a same-generation rescan and the ValueKey survives a reload; else fall back to the first match at/after the cursor.
        if (priorLocator is { } locator && _findMatchSet.Contains(locator))
        {
            SetCurrentMatchIndex(_findMatches.IndexOf(locator));

            return;
        }

        if (_findCurrentKey is { } key && _activeDisplayedEvents.ResolveByKey(key) is { } resolved)
        {
            int existing = _findMatches.IndexOf(resolved);

            if (existing >= 0)
            {
                SetCurrentMatchIndex(existing);

                return;
            }
        }

        SetCurrentMatchIndex(FirstMatchAtOrAfterAnchor());
    }

    private async Task RunFindScanAsync(
        IEventColumnView view,
        ColumnName[] columns,
        TimeZoneInfo timeZone,
        string query,
        bool caseSensitive,
        bool wholeWord,
        int epoch,
        CancellationToken token)
    {
        List<EventLocator>? matches = null;
        List<long>? matchTicks = null;

        try
        {
            (matches, matchTicks) = await Task.Run(
                () =>
                {
                    var found = new List<EventLocator>();
                    var foundTicks = new List<long>();
                    int total = view.Count;

                    for (int start = 0; start < total; start += FindChunkSize)
                    {
                        token.ThrowIfCancellationRequested();

                        int count = Math.Min(FindChunkSize, total - start);
                        IReadOnlyList<DisplayRow> slice = view.Slice(start, count);

                        foreach (DisplayRow row in slice)
                        {
                            if (EventFindMatcher.RowMatches(row.Lean, columns, timeZone, query, caseSensitive, wholeWord))
                            {
                                found.Add(row.Loc);
                                foundTicks.Add(row.Lean.TimeCreated.Ticks);
                            }
                        }
                    }

                    return (found, foundTicks);
                },
                token);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e)
        {
            TraceLogger.Error($"{nameof(LogTablePane)}: find scan failed: {e}");
        }

        try
        {
            await InvokeAsync(() =>
            {
                // Publish only if this scan is still the current one: same epoch, same view instance, same query terms.
                if (epoch != _findScanEpoch ||
                    !ReferenceEquals(view, _activeDisplayedEvents) ||
                    !string.Equals(query, _findQuery, StringComparison.Ordinal) ||
                    caseSensitive != _findCaseSensitive ||
                    wholeWord != _findWholeWord)
                {
                    return;
                }

                if (matches is null || matchTicks is null)
                {
                    _findScanning = false;
                    RequestFindRender();

                    return;
                }

                PublishFindMatches(matches, matchTicks);
            });
        }
        catch (ObjectDisposedException) { /* Component torn down mid-publish; nothing to update. */ }
    }

    private void ScheduleFindScan()
    {
        _findDebounceCts?.Cancel();
        _findDebounceCts?.Dispose();
        _findDebounceCts = null;

        if (_findQuery.Length == 0)
        {
            CancelFindScans();
            ClearFindMatches();

            return;
        }

        // Mark scanning immediately so stepping is blocked during the debounce window (otherwise Enter would navigate the pre-edit result set).
        _findScanning = true;
        _findWrapAnnouncement = string.Empty;

        var cts = new CancellationTokenSource();
        _findDebounceCts = cts;

        _ = DebounceThenScanAsync(cts);
    }

    private async Task ScrollToCurrentFindMatchAsync()
    {
        if (_findCurrentIndex < 0 || _findCurrentIndex >= _findMatches.Count)
        {
            _findScrollToCurrentOnRender = false;

            return;
        }

        EventLocator locator = _findMatches[_findCurrentIndex];
        int index = RowIndexOf(locator);

        if (index < 0)
        {
            _findScrollToCurrentOnRender = false;

            return;
        }

        if (_rowView is { } view)
        {
            EventGroup targetGroup = view.GroupForEvent(index);

            if (RecollapseSteppedAwayGroups(view, targetGroup.Key)) { return; }

            if (targetGroup.IsCollapsed)
            {
                _findExpandedGroupKeys.Add(targetGroup.Key);
                _findGroupContext = CurrentFindGroupContext();
                SetGroupCollapsed(targetGroup.Key, collapse: false);

                return;
            }
        }

        _findScrollToCurrentOnRender = false;
        int targetRow = _rowView?.VisibleRowForEvent(index) ?? index;

        if (_tableModule is not null)
        {
            await _tableModule.InvokeVoidAsync("scrollToRow", targetRow);
        }
    }

    private void SetCurrentMatchIndex(int index)
    {
        _findCurrentIndex = index;
        _findCurrentLocator = index >= 0 && index < _findMatches.Count ? _findMatches[index] : null;
        UpdateCurrentMatchKey();
    }

    private void StartFindScan()
    {
        CancelFindScans();

        if (_findQuery.Length == 0)
        {
            ClearFindMatches();
            RequestFindRender();

            return;
        }

        int epoch = ++_findScanEpoch;
        IEventColumnView view = _activeDisplayedEvents;
        var columns = (ColumnName[])_enabledColumns.Clone();
        TimeZoneInfo timeZone = _timeZoneSettings;
        string query = _findQuery;
        bool caseSensitive = _findCaseSensitive;
        bool wholeWord = _findWholeWord;

        _findScanning = true;
        var cts = new CancellationTokenSource();
        _findScanCts = cts;

        _ = RunFindScanAsync(view, columns, timeZone, query, caseSensitive, wholeWord, epoch, cts.Token);

        RequestFindRender();
    }

    private void StepFind(int direction)
    {
        FlushPendingFindScan();

        if (_findScanning || _findMatches.Count == 0) { return; }

        int previousIndex = _findCurrentIndex < 0 ? (direction > 0 ? -1 : 0) : _findCurrentIndex;
        int next = (previousIndex + direction + _findMatches.Count) % _findMatches.Count;

        _findWrapAnnouncement = direction > 0 && next <= previousIndex ? "Wrapped to first match"
            : direction < 0 && next >= previousIndex ? "Wrapped to last match"
            : string.Empty;

        SetCurrentMatchIndex(next);
        _findScrollToCurrentOnRender = true;

        RequestFindRender();
    }

    private bool TryGetCurrentMatchLocator(out EventLocator locator)
    {
        if (_findCurrentLocator is { } current)
        {
            locator = current;

            return true;
        }

        locator = default;

        return false;
    }

    private void UpdateCurrentMatchKey() =>
        _findCurrentKey =
            _findCurrentLocator is { } locator &&
            ValueKey.TryCreate(_activeDisplayedEvents.GetDetailLean(locator), out ValueKey key)
                ? key
                : null;

    private void UserSetGroupCollapsed(string key, bool collapse)
    {
        _findExpandedGroupKeys.Remove(key);
        SetGroupCollapsed(key, collapse);
    }

    private readonly record struct FindSegment(string Text, bool IsMark);
}
