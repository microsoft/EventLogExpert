// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.Concurrency;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.UI.LogTable.Find;
using EventLogExpert.UI.LogTable.Grouping;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EventLogExpert.UI.LogTable;

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
    private HashSet<EventLocator> _findMatchSet = [];
    private List<EventLocator> _findMatches = [];
    private bool _findOpen;
    private string _findQuery = string.Empty;
    private IDisposable? _findRegistration;
    private bool _findRenderRequested;
    private CancellationTokenSource? _findScanCts;
    private int _findScanEpoch;
    private bool _findScanning;
    private bool _findScrollToCurrentOnRender;
    private bool _findWholeWord;
    private FindWrapState _findWrapAnnouncement;

    [Inject]
    private IFindCoordinator FindCoordinator { get; init; } = null!;

    private int FindCurrentOrdinal => _findCurrentIndex >= 0 ? _findCurrentIndex + 1 : 0;

    [Inject]
    private IFindMarkerSource FindMarkerSource { get; init; } = null!;

    private int FindMatchCount => _findMatches.Count;

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
        _findWrapAnnouncement = FindWrapState.None;

        FindMarkerSource.Clear();
    }

    private Task CloseFind()
    {
        _findOpen = false;
        CancelFindScans();
        _findDebounceCts?.Cancel();
        _findDebounceCts?.Dispose();
        _findDebounceCts = null;

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
        (Presentation.ActiveTabId, Presentation.Ordering.GroupBy);

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

    private Task OnGroupCollapseRequestedAsync()
    {
        if (_disposed) { return Task.CompletedTask; }

        _findExpandedGroupKeys.Clear();

        return Task.CompletedTask;
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

    private void PublishFindMarks(List<long> matchTicks)
    {
        if (Presentation.ActiveTabId is not { } tableId)
        {
            FindMarkerSource.Clear();

            return;
        }

        long[] sorted = [.. matchTicks];
        Array.Sort(sorted);

        FindMarkerSource.Publish(tableId, sorted);
    }

    private void PublishFindMatches(List<EventLocator> matches, List<long> matchTicks)
    {
        EventLocator? priorLocator = _findCurrentLocator;

        _findMatches = matches;
        _findMatchSet = new HashSet<EventLocator>(matches);
        _findScanning = false;
        _findWrapAnnouncement = FindWrapState.None;

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
            (matches, matchTicks) = await CpuScheduler.RunAsync(
                findToken =>
                {
                    var found = new List<EventLocator>();
                    var foundTicks = new List<long>();
                    int total = view.Count;

                    for (int start = 0; start < total; start += FindChunkSize)
                    {
                        findToken.ThrowIfCancellationRequested();

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
                CpuWorkPriority.Interactive,
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

        _findScanning = true;
        _findWrapAnnouncement = FindWrapState.None;

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

        _findWrapAnnouncement = direction > 0 && next <= previousIndex ?
            FindWrapState.WrappedToFirst :
                direction < 0 && next >= previousIndex ?
                    FindWrapState.WrappedToLast : FindWrapState.None;

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
