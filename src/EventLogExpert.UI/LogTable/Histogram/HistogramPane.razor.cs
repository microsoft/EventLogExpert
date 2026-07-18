// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.Common.Interop;
using EventLogExpert.UI.LogTable.Find;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Globalization;

namespace EventLogExpert.UI.LogTable.Histogram;

public sealed partial class HistogramPane
{
    private const int AnnounceDelayMs = 500;
    private const int AxisReservePx = 16;
    private const double KeyboardPanFraction = 0.2;
    private const int MaxWindowHistory = 100;
    private const int MinBarPx = 14;
    private const int MinWindowBaseBins = 4;
    private const double MinWindowFraction = (double)MinWindowBaseBins / HistogramConstants.MaxBuckets;
    private const int RecomputeThrottleMs = 500;
    private const double ZoomInFactor = 0.8;
    private const double ZoomOutFactor = 1.25;

    private readonly HashSet<string> _hiddenGroups = [];
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly List<(long Start, long End, bool Zoomed)> _windowHistory = [];

    private int _announceGeneration;
    private string _announcement = string.Empty;
    private long _appliedDimensionToken;
    private HistogramData? _baseData;
    private string _binAnnouncement = string.Empty;
    private int? _binCursor;
    private HistogramDimension _dimension = HistogramDimension.Severity;
    private bool _disposed;
    private DotNetObjectReference<HistogramPane>? _dotNetRef;
    private long[] _findTicks = [];
    private long? _focusedTicks;
    private bool _isZoomed;
    private IJSObjectReference? _module;
    // Generation for queued pan/zoom: bumped on undo so a pan/zoom initiated before the undo (its stale token captured at schedule time) no-ops instead of reapplying the pre-undo window.
    private int _navToken;
    private double? _pendingViewStartFraction;
    private int _plotHeightPx;
    private bool _recomputePending;
    private HistogramRender? _render;
    private CancellationTokenSource? _scanCts;
    private int _scanEpoch;
    private int _segmentGroupCount;
    private int[] _segmentHeights = [];
    private TimeZoneInfo _timeZone = TimeZoneInfo.Utc;
    private int _viewportWidthPx;
    private int[] _visibleGroupCounts = [];
    private long _windowEndTicks;
    private long _windowStartTicks;

    [Inject] private IStateSelection<LogTableState, EventLogId?> ActiveEventLogId { get; init; } = null!;

    [Inject] private IStateSelection<LogTableState, string?> ActiveOriginLog { get; init; } = null!;

    [Inject] private IStateSelection<LogTableState, IEventColumnView> ActiveView { get; init; } = null!;

    [Inject] private IStateSelection<HistogramState, HistogramDimensionRequest?> DimensionRequest { get; init; } = null!;

    [Inject] private IFilterLensCommands FilterLensCommands { get; init; } = null!;

    [Inject] private IFindMarkerSource FindMarkers { get; init; } = null!;

    [Inject] private IStateSelection<EventLogState, SelectionEntry?> Focus { get; init; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private ISettingsService Settings { get; init; } = null!;

    [Inject] private ITraceLogger TraceLogger { get; init; } = null!;

    [JSInvokable]
    public void OnHistogramDragSelected(double startFraction, double endFraction, bool scope)
    {
        if (_disposed || _baseData is null) { return; }

        long startTicks = WindowFractionToTicks(startFraction);
        long endTicks = WindowFractionToTicks(endFraction);
        SetWindow(startTicks, endTicks);

        if (scope) { ScopeToRange(); }
    }

    [JSInvokable]
    public void OnHistogramPanned(double windowStartFraction, int navToken)
    {
        if (_disposed || navToken != _navToken || _baseData is not { } data || !_isZoomed) { return; }

        int binCount = WindowBinCount(data);
        int newStartBin = (int)Math.Round(Math.Clamp(windowStartFraction, 0, 1) * data.BinCount);

        SetWindowByBins(data, newStartBin, binCount);

        AggregateAndRender(syncScrollbar: false);
    }

    [JSInvokable]
    public void OnHistogramReset()
    {
        if (_disposed) { return; }

        Fit();
    }

    [JSInvokable]
    public void OnHistogramResized(int widthPx, int heightPx)
    {
        if (_disposed) { return; }

        bool hadDimensions = _viewportWidthPx > 0 && _plotHeightPx > 0;
        _viewportWidthPx = widthPx;
        _plotHeightPx = heightPx;

        if (widthPx <= 0 || heightPx <= 0) { return; }

        // A widened or newly-revealed viewport can push the current zoom depth past the track-width cap, so re-clamp the retained window and render immediately: the inline track width is a percentage the browser re-resolves against the new viewport at once, so deferring the render to an async rescan would briefly expose the stale over-cap track.
        if (_baseData is { } data)
        {
            SetWindowByBins(data, WindowStartBin(data), WindowBinCount(data));
            AggregateAndRender();
        }

        if (!hadDimensions) { StartScan(); }
    }

    [JSInvokable]
    public void OnHistogramScopeBin(double fraction)
    {
        if (_disposed || _render is not { Bins.Count: > 0 } render) { return; }

        int index = Math.Clamp((int)(Math.Clamp(fraction, 0, 1) * render.Bins.Count), 0, render.Bins.Count - 1);
        var bin = render.Bins[index];

        FilterLensCommands.ShowTimeRange(
            new DateTime(bin.StartTicks, DateTimeKind.Utc),
            new DateTime(bin.EndTicks, DateTimeKind.Utc),
            _timeZone,
            ActiveOriginLog.Value);
    }

    [JSInvokable]
    public void OnHistogramUndo()
    {
        if (_disposed) { return; }

        UndoZoom();
    }

    [JSInvokable]
    public void OnHistogramZoomed(bool zoomIn, double cursorFraction, int navToken)
    {
        if (_disposed || navToken != _navToken) { return; }

        ApplyZoom(zoomIn ? ZoomInFactor : ZoomOutFactor, Math.Clamp(cursorFraction, 0, 1));
    }

    protected override async ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;

            ActiveView.SelectedValueChanged -= OnActiveViewChanged;
            ActiveEventLogId.SelectedValueChanged -= OnActiveEventLogIdChanged;
            DimensionRequest.SelectedValueChanged -= OnDimensionRequestChanged;
            Focus.SelectedValueChanged -= OnFocusChanged;
            Settings.TimeZoneChanged -= OnTimeZoneChanged;
            FindMarkers.MarksChanged -= OnFindMarksChanged;

            _lifetimeCts.Cancel();
            _lifetimeCts.Dispose();

            try { _scanCts?.Cancel(); } catch (ObjectDisposedException) { /* Already disposed; cancel is moot. */ }

            _scanCts?.Dispose();
            _scanCts = null;

            await JsModuleInterop.DisposeModuleSafelyAsync(
                _module,
                static module => module.InvokeVoidAsync("disposeHistogram"));

            _module = null;

            _dotNetRef?.Dispose();
        }

        await base.DisposeAsyncCore(disposing);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _dotNetRef = DotNetObjectReference.Create(this);

            _module = await JSRuntime.InvokeAsync<IJSObjectReference>(
                "import",
                "./_content/EventLogExpert.UI/LogTable/Histogram/HistogramPane.razor.js");

            await _module.InvokeVoidAsync("initHistogram", _dotNetRef);
        }

        if (_pendingViewStartFraction is { } startFraction && _module is not null)
        {
            _pendingViewStartFraction = null;

            try { await _module.InvokeVoidAsync("applyView", startFraction, _navToken); }
            catch (JSDisconnectedException) { /* Circuit torn down; nothing to sync. */ }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override void OnInitialized()
    {
        _timeZone = Settings.TimeZoneInfo;
        ActiveView.Select(state => state.GetActiveDisplayedEvents());
        ActiveOriginLog.Select(SelectActiveOriginLog);
        ActiveEventLogId.Select(state => state.ActiveEventLogId);
        Focus.Select(state => state.Focus);
        DimensionRequest.Select(state => state.DimensionRequest);
        ActiveView.SelectedValueChanged += OnActiveViewChanged;
        ActiveEventLogId.SelectedValueChanged += OnActiveEventLogIdChanged;
        Focus.SelectedValueChanged += OnFocusChanged;
        DimensionRequest.SelectedValueChanged += OnDimensionRequestChanged;
        Settings.TimeZoneChanged += OnTimeZoneChanged;
        FindMarkers.MarksChanged += OnFindMarksChanged;

        var initialRequest = DimensionRequest.Value;

        if (initialRequest is not null && initialRequest.Token > _appliedDimensionToken)
        {
            _dimension = initialRequest.Dimension;
            _appliedDimensionToken = initialRequest.Token;
        }

        RefreshFindTicks();

        base.OnInitialized();
    }

    private static string DimensionLabel(HistogramDimension dimension) => dimension switch
    {
        HistogramDimension.EventId => "Event ID",
        HistogramDimension.LogonType => "Logon Type",
        HistogramDimension.TaskCategory => "Task Category",
        HistogramDimension.TicketEncryptionType => "Ticket Encryption Type",
        HistogramDimension.ErrorCode => "Error Code",
        _ => dimension.ToString()
    };

    // ErrorCode charts a failure subset, so its empty-state reports the absence of failures (not of the field, which may be
    // present as errorCode = 0); visibleRange distinguishes a zoomed window with no failures from the whole view having none.
    private static string EmptyStateMessage(HistogramDimension dimension, bool visibleRange) => dimension switch
    {
        HistogramDimension.ErrorCode => visibleRange
            ? "No update error codes in the visible range."
            : "No update error codes in this view.",
        _ => visibleRange
            ? "No events to chart in the current view."
            : $"No {DimensionLabel(dimension)} values in this view."
    };

    private static string FindMarkerPoints(double centerX) =>
        $"{FormatCoordinate(centerX - 3)},0 {FormatCoordinate(centerX + 3)},0 {FormatCoordinate(centerX)},5";

    private static string FormatCoordinate(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string? SelectActiveOriginLog(LogTableState state)
    {
        var activeTab = state.EventTables.FirstOrDefault(tab => tab.Id == state.ActiveEventLogId);

        return activeTab is { IsCombined: false } ? activeTab.LogName : null;
    }

    private void AggregateAndRender(bool syncScrollbar = true)
    {
        _binCursor = null;

        if (_baseData is not { } data || data.GroupingFieldAbsent)
        {
            _render = null;

            if (_baseData is { GroupingFieldAbsent: true })
            {
                // Match the visible empty-state so a screen reader is told why the timeline is blank, and drop any stale bin readout.
                _binAnnouncement = string.Empty;
                _announcement = EmptyStateMessage(_dimension, visibleRange: false);
            }

            StateHasChanged();

            return;
        }

        _render = HistogramAggregator.Aggregate(data, _windowStartTicks, _windowEndTicks, TargetBins(data));
        ComputeSegmentHeights(_render, data.Groups.Count);

        if (syncScrollbar) { _pendingViewStartFraction = StartFraction(); }

        ScheduleAnnouncement();
        StateHasChanged();
    }

    private async Task AnnounceAfterDelayAsync(int generation)
    {
        try { await Task.Delay(AnnounceDelayMs, _lifetimeCts.Token); }
        catch (OperationCanceledException) { return; }

        try
        {
            await InvokeAsync(() =>
            {
                if (generation != _announceGeneration || _disposed || _render is not { } render || _baseData is not { } data) { return; }

                _announcement = HistogramSummary.WindowAnnouncement(render, data.Groups, data.EventNoun, _timeZone);
                StateHasChanged();
            });
        }
        catch (ObjectDisposedException) { /* Component torn down mid-announce; nothing to update. */ }
    }

    private void ApplyDimension(HistogramDimension dimension, bool force)
    {
        if (!force && dimension == _dimension) { return; }

        _dimension = dimension;
        _hiddenGroups.Clear();

        // A retained absent-field result would otherwise render its empty-state under the newly-selected dimension's name
        // until the rescan lands; clear the live-region status with it so a screen reader isn't left holding the previous
        // dimension's "no values" message during the gap.
        if (_baseData is { GroupingFieldAbsent: true })
        {
            _baseData = null;
            _render = null;
            _announcement = string.Empty;
            _binAnnouncement = string.Empty;
        }

        RecomputeSegmentHeights();

        StartScan();
    }

    private void ApplyPublishedWindow()
    {
        if (_baseData is not { } data) { return; }

        // A disjoint-domain reload leaves both the pinned window and the absolute-tick undo history meaningless - including after a Fit cleared _isZoomed but left history - so drop the history and supersede queued nav whenever the retained window falls entirely outside the new domain, before the not-zoomed refit. A same-tab clear/refill reaches here (not OnActiveEventLogIdChanged), so the token bump must live here too.
        bool windowDisjoint = _windowEndTicks < data.MinUtc.Ticks || _windowStartTicks > data.MaxUtc.Ticks;

        if (windowDisjoint)
        {
            _windowHistory.Clear();
            SupersedeQueuedNavigation();
        }

        if (!_isZoomed || windowDisjoint)
        {
            SetWindowByBins(data, 0, data.BinCount);

            return;
        }

        SetWindowByBins(data, WindowStartBin(data), WindowBinCount(data));
    }

    private void ApplyZoom(double factor, double anchorFraction)
    {
        if (_baseData is not { } data) { return; }

        int totalBins = data.BinCount;
        int minBins = Math.Min(MinWindowBaseBins, totalBins);
        int currentBins = WindowBinCount(data);
        int newBins = (int)Math.Round(currentBins * factor);

        if (factor < 1 && newBins >= currentBins) { newBins = currentBins - 1; }
        
        if (factor > 1 && newBins <= currentBins) { newBins = currentBins + 1; }

        newBins = Math.Clamp(newBins, minBins, totalBins);

        double anchorBin = WindowStartBin(data) + (anchorFraction * currentBins);
        int newStartBin = (int)Math.Round(anchorBin - (anchorFraction * newBins));

        SetWindowByBins(data, newStartBin, newBins, recordHistory: true);

        AggregateAndRender();
    }

    private IReadOnlyList<AxisLabel> AxisLabels()
    {
        var labels = new List<AxisLabel>();

        if (_render is not { } render || _viewportWidthPx <= 0) { return labels; }

        long span = render.WindowEndTicks - render.WindowStartTicks;
        int count = Math.Clamp(_viewportWidthPx / 130, 2, 6);
        bool crossesDay = WindowCrossesDay();

        for (int index = 0; index < count; index++)
        {
            double fraction = (double)index / (count - 1);
            double x = fraction * _viewportWidthPx;
            long ticks = Math.Clamp(
                render.WindowStartTicks + (long)(fraction * span),
                render.WindowStartTicks,
                render.WindowEndTicks);
            var display = ToDisplay(new DateTime(ticks, DateTimeKind.Utc));
            string text = crossesDay
                ? $"{display:d} {display:HH:mm}"
                : $"{display:HH:mm:ss}";
            string anchor = index == 0 ? "start" : index == count - 1 ? "end" : "middle";

            labels.Add(new AxisLabel(x, text, anchor));
        }

        return labels;
    }

    private int BarsAreaHeight() => Math.Max(0, _plotHeightPx - AxisReservePx);

    private string BarTooltip(HistogramRenderBin bin)
    {
        var start = ToDisplay(new DateTime(bin.StartTicks, DateTimeKind.Utc));
        var end = ToDisplay(new DateTime(Math.Max(bin.StartTicks, bin.EndTicks), DateTimeKind.Utc));
        bool crossesDay = WindowCrossesDay();
        string startText = crossesDay ? $"{start:d} {start:HH:mm:ss}" : $"{start:HH:mm:ss}";
        string endText = crossesDay ? $"{end:d} {end:HH:mm:ss}" : $"{end:HH:mm:ss}";

        return $"{bin.Total} {_baseData?.EventNoun ?? "events"}{GroupBreakdown(bin)}, {startText} - {endText}";
    }

    private string BinCursorAnnouncement(HistogramRenderBin bin)
    {
        var start = ToDisplay(new DateTime(bin.StartTicks, DateTimeKind.Utc));
        var end = ToDisplay(new DateTime(Math.Max(bin.StartTicks, bin.EndTicks), DateTimeKind.Utc));
        string anomaly = bin.IsAnomaly ? ", spike" : string.Empty;

        return $"{start:g} to {end:g}: {bin.Total} {_baseData?.EventNoun ?? "events"}{GroupBreakdown(bin)}{anomaly}.";
    }

    private void ClearBinCursor()
    {
        if (_binCursor is null) { return; }

        _binCursor = null;

        StateHasChanged();
    }

    private void ComputeSegmentHeights(HistogramRender render, int groupCount)
    {
        _segmentGroupCount = groupCount;

        int needed = render.Bins.Count * groupCount;

        if (_segmentHeights.Length < needed) { _segmentHeights = new int[needed]; }
        if (_visibleGroupCounts.Length != groupCount) { _visibleGroupCounts = new int[groupCount]; }

        double barsHeight = BarsAreaHeight();

        Span<bool> hidden = groupCount <= 16 ? stackalloc bool[16] : new bool[groupCount];
        hidden = hidden[..groupCount];

        for (int group = 0; group < groupCount; group++) { hidden[group] = IsGroupHidden(group); }

        // Normalize bar heights to the tallest VISIBLE bin (hidden legend groups excluded) so toggling a group off rescales the remaining bars to fill the plot, instead of leaving the true tallest bar a 1-2px sliver under a hidden group's scale.
        int maxVisibleBinTotal = 0;

        for (int bin = 0; bin < render.Bins.Count; bin++)
        {
            int[] counts = render.Bins[bin].GroupCounts;
            int visible = 0;

            for (int group = 0; group < groupCount; group++)
            {
                if (!hidden[group]) { visible += counts[group]; }
            }

            if (visible > maxVisibleBinTotal) { maxVisibleBinTotal = visible; }
        }

        for (int bin = 0; bin < render.Bins.Count; bin++)
        {
            int[] counts = render.Bins[bin].GroupCounts;

            for (int group = 0; group < groupCount; group++)
            {
                _visibleGroupCounts[group] = hidden[group] ? 0 : counts[group];
            }

            HistogramScale.WriteStackedGroupHeights(
                _visibleGroupCounts,
                maxVisibleBinTotal,
                barsHeight,
                _segmentHeights.AsSpan(bin * groupCount, groupCount));
        }
    }

    private void Fit()
    {
        if (_baseData is not { } data) { return; }

        SupersedeQueuedNavigation();
        SetWindowByBins(data, 0, data.BinCount, recordHistory: true);

        AggregateAndRender();
    }

    private double? FocusMarkerX()
    {
        if (_focusedTicks is not { } ticks || _viewportWidthPx <= 0 || _render is not { } render) { return null; }

        long span = render.WindowEndTicks - render.WindowStartTicks;

        if (span <= 0 || ticks < render.WindowStartTicks || ticks > render.WindowEndTicks) { return null; }

        return (double)(ticks - render.WindowStartTicks) / span * _viewportWidthPx;
    }

    private string GroupBreakdown(HistogramRenderBin bin)
    {
        if (_baseData is not { } data) { return string.Empty; }

        var parts = new List<string>();

        for (int group = data.Groups.Count - 1; group >= 0; group--)
        {
            int count = bin.GroupCounts[group];

            if (count > 0) { parts.Add($"{count} {data.Groups[group].Label}"); }
        }

        return parts.Count == 0 ? string.Empty : $" ({string.Join(", ", parts)})";
    }

    private string GroupColorClass(int group) =>
        _baseData is { } data && group < data.Groups.Count ? data.Groups[group].ColorClass : string.Empty;

    private void HandleKeyDown(KeyboardEventArgs args)
    {
        if (args is { ShiftKey: true, Key: "ArrowLeft" or "ArrowRight" })
        {
            MoveBinCursor(args.Key == "ArrowRight" ? 1 : -1);

            return;
        }

        switch (args.Key)
        {
            case "ArrowLeft": PanByFraction(-KeyboardPanFraction); break;
            case "ArrowRight": PanByFraction(KeyboardPanFraction); break;
            case "ArrowUp" or "+" or "=": ZoomFromControl(ZoomInFactor); break;
            case "ArrowDown" or "-" or "_": ZoomFromControl(ZoomOutFactor); break;
            case "Home" or "0": Fit(); break;
            case "Escape": ClearBinCursor(); break;
            case "Enter": ScopeBinCursorOrWindow(); break;
        }
    }

    private bool HasFindHit(long startTicks, long endTicks)
    {
        if (_findTicks.Length == 0) { return false; }

        int index = Array.BinarySearch(_findTicks, startTicks);

        if (index < 0) { index = ~index; }

        return index < _findTicks.Length && _findTicks[index] <= endTicks;
    }

    private bool IsGroupHidden(int group) =>
        _baseData is { } data && group < data.Groups.Count && _hiddenGroups.Contains(data.Groups[group].Key);

    private void MoveBinCursor(int delta)
    {
        if (_render is not { Bins.Count: > 0 } render) { return; }

        int next = _binCursor is { } cursor ? cursor + delta : delta > 0 ? 0 : render.Bins.Count - 1;
        _binCursor = Math.Clamp(next, 0, render.Bins.Count - 1);
        _binAnnouncement = BinCursorAnnouncement(render.Bins[_binCursor.Value]);

        StateHasChanged();
    }

    // A tab switch makes the absolute-tick zoom window meaningless, so drop the zoom and rescan at the new tab's full span.
    private void OnActiveEventLogIdChanged(object? sender, EventLogId? logId) => _ = InvokeAsync(() =>
    {
        _isZoomed = false;
        _windowHistory.Clear();
        SupersedeQueuedNavigation();
        // Invalidate the old tab's data + render so nothing acts on stale data during the gap, then schedule the rescan directly: a one-member group's header and member tabs share one view, so OnActiveViewChanged is not guaranteed to fire and can't be relied on to republish (ScheduleRecompute de-dupes with it when it does).
        _baseData = null;
        _render = null;
        RefreshFindTicks();
        ScheduleRecompute();
    });

    private void OnActiveViewChanged(object? sender, IEventColumnView view) => _ = InvokeAsync(ScheduleRecompute);

    private void OnDimensionRequestChanged(object? sender, HistogramDimensionRequest? request) => _ = InvokeAsync(() =>
    {
        if (_disposed) { return; }

        var current = DimensionRequest.Value;
        if (current is null || current.Token <= _appliedDimensionToken) { return; }

        _appliedDimensionToken = current.Token;
        ApplyDimension(current.Dimension, force: true);
    });

    private void OnDimensionSelected(HistogramDimension dimension)
    {
        ApplyDimension(dimension, force: false);
    }

    private void OnFindMarksChanged(object? sender, EventArgs args) => _ = InvokeAsync(() =>
    {
        if (_disposed) { return; }

        RefreshFindTicks();
        StateHasChanged();
    });

    private void OnFocusChanged(object? sender, SelectionEntry? focus) => _ = InvokeAsync(() =>
    {
        ResolveFocusedTicks();
        StateHasChanged();
    });

    private void OnTimeZoneChanged(object? sender, TimeZoneInfo timeZone) => _ = InvokeAsync(() =>
    {
        if (_disposed) { return; }

        _timeZone = timeZone;

        // Both live regions embed _timeZone-formatted times, so re-render alone would leave a screen reader announcing the old zone until the next pan/zoom/scan.
        ScheduleAnnouncement();

        if (_binCursor is { } cursor && _render is { Bins.Count: > 0 } render && cursor < render.Bins.Count)
        {
            _binAnnouncement = BinCursorAnnouncement(render.Bins[cursor]);
        }

        StateHasChanged();
    });

    private void PanByFraction(double fraction)
    {
        if (_baseData is not { } data || !_isZoomed) { return; }

        SupersedeQueuedNavigation();

        int binCount = WindowBinCount(data);
        int delta = (int)Math.Round(fraction * binCount);

        if (delta == 0) { delta = fraction > 0 ? 1 : -1; }

        SetWindowByBins(data, WindowStartBin(data) + delta, binCount);

        AggregateAndRender();
    }

    private void PushWindowHistory()
    {
        (long, long, bool) current = (_windowStartTicks, _windowEndTicks, _isZoomed);

        if (_windowHistory.Count > 0 && _windowHistory[^1] == current) { return; }

        _windowHistory.Add(current);

        if (_windowHistory.Count > MaxWindowHistory) { _windowHistory.RemoveAt(0); }
    }

    // Segment heights are scaled to the visible groups, so any change to the hidden set must rescale the current render's bars (the pending scan, if any, replaces them once it lands).
    private void RecomputeSegmentHeights()
    {
        if (_render is { } render && _baseData is { } data) { ComputeSegmentHeights(render, data.Groups.Count); }
    }

    private void RefreshFindTicks() =>
        _findTicks = FindMarkers.Owner == ActiveEventLogId.Value && FindMarkers.Ticks is { Count: > 0 } ticks
            ? [.. ticks]
            : [];

    private string RegionAria() =>
        _baseData is { } data ? HistogramSummary.RegionLabel(data, _timeZone) : "Timeline";

    private void ResolveFocusedTicks()
    {
        _focusedTicks = Focus.Value?.CurrentHandle is { } handle
            && ActiveView.Value.Rank(handle) >= 0
            && ActiveView.Value.TryGetTimeTicks(handle, out long ticks)
            ? ticks
            : null;
    }

    private async Task RunScanAsync(IEventColumnView view, HistogramDimension dimension, int epoch, CancellationToken token)
    {
        HistogramData? data;

        try
        {
            // Domain is the view's own survivor span, so the bucketer's edge clamp can't pile off-window rows into false spikes.
            data = await Task.Run(() => HistogramBuilder.Build(view, dimension, HistogramConstants.MaxBuckets, token), token);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e)
        {
            TraceLogger.Error($"{nameof(HistogramPane)}: histogram scan failed: {e}");

            return;
        }

        try
        {
            await InvokeAsync(() =>
            {
                if (_disposed || epoch != _scanEpoch || !ReferenceEquals(view, ActiveView.Value)) { return; }

                _baseData = data;
                ResolveFocusedTicks();
                ApplyPublishedWindow();
                AggregateAndRender();
            });
        }
        catch (ObjectDisposedException) { /* Component torn down mid-publish; nothing to update. */ }
    }

    private void ScheduleAnnouncement() => _ = AnnounceAfterDelayAsync(++_announceGeneration);

    private void ScheduleRecompute()
    {
        if (_recomputePending || _disposed) { return; }

        _recomputePending = true;
        _ = ThrottleThenScanAsync();
    }

    private void ScopeBin(HistogramRenderBin bin) =>
        FilterLensCommands.ShowTimeRange(
            new DateTime(bin.StartTicks, DateTimeKind.Utc),
            new DateTime(bin.EndTicks, DateTimeKind.Utc),
            _timeZone,
            ActiveOriginLog.Value);

    private void ScopeBinCursorOrWindow()
    {
        if (_binCursor is { } cursor && _render is { } render && cursor < render.Bins.Count)
        {
            ScopeBin(render.Bins[cursor]);

            return;
        }

        ScopeToRange();
    }

    private void ScopeToRange()
    {
        if (_render is not { } render) { return; }

        FilterLensCommands.ShowTimeRange(
            new DateTime(render.WindowStartTicks, DateTimeKind.Utc),
            new DateTime(render.WindowEndTicks, DateTimeKind.Utc),
            _timeZone,
            ActiveOriginLog.Value);
    }

    private void SetWindow(long startTicks, long endTicks)
    {
        if (_baseData is not { } data) { return; }

        SupersedeQueuedNavigation();

        long span = data.BucketSpanTicks;
        long baseMin = data.MinUtc.Ticks;
        int totalBins = data.BinCount;
        int loBin = (int)Math.Clamp((Math.Min(startTicks, endTicks) - baseMin) / span, 0, totalBins - 1);
        int hiBin = (int)Math.Clamp((Math.Max(startTicks, endTicks) - baseMin) / span, 0, totalBins - 1);

        SetWindowByBins(data, loBin, hiBin - loBin + 1, recordHistory: true);

        AggregateAndRender();
    }

    // Every zoom/pan/fit sets the window here as a whole base-bin range, so the render bounds equal the window and incremental zoom never re-quantizes back to the same bins (no stall).
    private void SetWindowByBins(HistogramData data, int startBin, int binCount, bool recordHistory = false)
    {
        int totalBins = data.BinCount;
        int minBins = Math.Min(MinWindowBaseBins, totalBins);

        // Keep the virtual scroll track under the browser's maximum layout width so scrollWidth == clientWidth / WindowFraction holds and the scrollbar can reach the final bins; a deeper zoom would overflow the cap.
        if (_viewportWidthPx > 0)
        {
            minBins = Math.Max(minBins, HistogramTrackCap.MinBinsForWidth(_viewportWidthPx, totalBins));
        }

        binCount = Math.Clamp(binCount, minBins, totalBins);
        startBin = Math.Clamp(startBin, 0, totalBins - binCount);

        long span = data.BucketSpanTicks;
        long baseMin = data.MinUtc.Ticks;

        bool newZoomed = binCount < totalBins;
        long newStartTicks = newZoomed ? baseMin + (startBin * span) : baseMin;
        long newEndTicks = newZoomed ? Math.Min((baseMin + ((startBin + binCount) * span)) - 1, data.MaxUtc.Ticks) : data.MaxUtc.Ticks;

        // Record an undo step only when the window actually moves, so a no-op zoom (clamped at the floor or full span) can't leave a dead history entry that swallows the first Undo.
        if (recordHistory && (newStartTicks != _windowStartTicks || newEndTicks != _windowEndTicks)) { PushWindowHistory(); }

        _isZoomed = newZoomed;
        _windowStartTicks = newStartTicks;
        _windowEndTicks = newEndTicks;
    }

    private double StartFraction()
    {
        if (_baseData is not { } data || data.BinCount <= 0) { return 0; }

        return Math.Clamp((double)WindowStartBin(data) / data.BinCount, 0, 1);
    }

    private void StartScan()
    {
        if (_disposed) { return; }

        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = null;

        if (_viewportWidthPx <= 0 || _plotHeightPx <= 0) { return; }

        IEventColumnView view = ActiveView.Value;
        int epoch = ++_scanEpoch;
        HistogramDimension dimension = _dimension;
        var cts = new CancellationTokenSource();
        _scanCts = cts;

        _ = RunScanAsync(view, dimension, epoch, cts.Token);
    }

    // Invalidate any pan/zoom queued before an explicit navigation reset (undo, Fit, drag-select, tab switch): the higher generation makes their stale, schedule-time-stamped OnHistogramZoomed/OnHistogramPanned invocations no-op. Every caller is followed by a render (immediate sync-scroll render, or the rescan's after a tab switch) that publishes the new token to JS via applyView.
    private void SupersedeQueuedNavigation() => _navToken++;

    private int TargetBins(HistogramData data)
    {
        return _viewportWidthPx <= 0 ? 1 : Math.Clamp((int)Math.Round(_viewportWidthPx / (double)MinBarPx), 1, data.BinCount);
    }

    private async Task ThrottleThenScanAsync()
    {
        try { await Task.Delay(RecomputeThrottleMs, _lifetimeCts.Token); }
        catch (OperationCanceledException) { return; }

        _recomputePending = false;
        StartScan();
    }

    private DateTime ToDisplay(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, _timeZone);

    private void ToggleGroup(string key)
    {
        // Keyed by stable Key so a live-tail re-rank can't swap which category is hidden. Window totals and anomaly flags stay over all groups.
        if (!_hiddenGroups.Remove(key)) { _hiddenGroups.Add(key); }

        RecomputeSegmentHeights();

        StateHasChanged();
    }

    private string TrackWidthStyle() => $"width:{FormatCoordinate(100 / WindowFraction())}%";

    private void UndoZoom()
    {
        if (_windowHistory.Count == 0 || _baseData is not { } data) { return; }

        SupersedeQueuedNavigation();

        long beforeStart = _windowStartTicks;
        long beforeEnd = _windowEndTicks;

        // Pop past any snapshot that, after the track-cap clamp, reproduces the current window (a resize-forced re-clamp can leave the top entry equal to the current view), so Undo always moves visibly.
        while (_windowHistory.Count > 0)
        {
            (long start, long end, bool zoomed) = _windowHistory[^1];
            _windowHistory.RemoveAt(_windowHistory.Count - 1);

            if (!zoomed)
            {
                SetWindowByBins(data, 0, data.BinCount);
            }
            else
            {
                long span = data.BucketSpanTicks;
                long baseMin = data.MinUtc.Ticks;
                int totalBins = data.BinCount;
                int startBin = (int)Math.Clamp((start - baseMin) / span, 0, totalBins - 1);
                int endBin = (int)Math.Clamp((end - baseMin) / span, 0, totalBins - 1);
                SetWindowByBins(data, startBin, endBin - startBin + 1);
            }

            if (_windowStartTicks != beforeStart || _windowEndTicks != beforeEnd) { break; }
        }

        AggregateAndRender();
    }

    private int WindowBinCount(HistogramData data)
    {
        int startBin = WindowStartBin(data);
        int endBin = (int)Math.Clamp((_windowEndTicks - data.MinUtc.Ticks) / data.BucketSpanTicks, 0, data.BinCount - 1);

        return endBin - startBin + 1;
    }

    // True when the window's displayed endpoints fall on different calendar days (including a short window straddling midnight), so times need a date to disambiguate.
    private bool WindowCrossesDay() =>
        _render is { } render &&
        ToDisplay(new DateTime(render.WindowStartTicks, DateTimeKind.Utc)).Date
            != ToDisplay(new DateTime(render.WindowEndTicks, DateTimeKind.Utc)).Date;

    private double WindowFraction()
    {
        if (_baseData is not { BinCount: > 0 } data) { return 1; }

        return Math.Clamp((double)WindowBinCount(data) / data.BinCount, MinWindowFraction, 1);
    }

    private long WindowFractionToTicks(double fraction)
    {
        if (_render is not { } render) { return _windowStartTicks; }

        return render.WindowStartTicks + (long)(Math.Clamp(fraction, 0, 1) * (render.WindowEndTicks - render.WindowStartTicks));
    }

    private int WindowStartBin(HistogramData data) =>
        (int)Math.Clamp((_windowStartTicks - data.MinUtc.Ticks) / data.BucketSpanTicks, 0, data.BinCount - 1);

    // Explicit keyboard/toolbar zoom supersedes any queued wheel/scroll momentum before zooming; the wheel path (OnHistogramZoomed) calls ApplyZoom directly with no bump, so rapid coalesced wheel zooming isn't self-invalidated.
    private void ZoomFromControl(double factor)
    {
        SupersedeQueuedNavigation();
        ApplyZoom(factor, 0.5);
    }

    private readonly record struct AxisLabel(double X, string Text, string Anchor);
}
