// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.Common.Interop;
using EventLogExpert.UI.LogTable.Find;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Collections.Immutable;
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
    private const int StackHiddenGroupThreshold = 16;
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
    private HistogramChartState _chartState = HistogramChartState.Pending;
    private HistogramDimension _dimension = HistogramDimension.Severity;
    private bool _disposed;
    private DotNetObjectReference<HistogramPane>? _dotNetRef;
    private long[] _findTicks = [];
    private long? _focusedTicks;
    private bool _isZoomed;
    private ViewContentToken? _lastScannedToken;
    private IJSObjectReference? _module;
    private int _navToken;
    private double? _pendingViewStartFraction;
    private int _plotHeightPx;
    private bool _recomputePending;
    private HistogramRender? _render;
    private CancellationTokenSource? _scanCts;
    private int _scanVersion;
    private int _segmentGroupCount;
    private int[] _segmentHeights = [];
    private SavedFilter[] _tieHighlightFilters = [];
    private int _tiePlanKey;
    private TimeZoneInfo _timeZone = TimeZoneInfo.Utc;
    private int _viewportWidthPx;
    private int[] _visibleGroupCounts = [];
    private long _windowEndTicks;
    private long _windowStartTicks;

    [Inject] private IActiveEventLogSource ActiveEventLog { get; init; } = null!;

    [Inject] private IHistogramDimensionRequestSource DimensionRequest { get; init; } = null!;

    [Inject] private IEventFocusSource EventFocus { get; init; } = null!;

    [Inject] private IFilterLensCommands FilterLensCommands { get; init; } = null!;

    [Inject] private IActiveFiltersSource Filters { get; init; } = null!;

    [Inject] private IFindMarkerSource FindMarkers { get; init; } = null!;

    [Inject] private IHighlightSelector HighlightSelector { get; init; } = null!;

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
            Presentation.ActiveLogName);
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

    internal static (string? CssName, string Description) ResolveGroupHighlight(
        uint mask,
        IReadOnlyList<SavedFilter> tieHighlightFilters)
    {
        bool hasUncolored = mask == 0 || (mask & 1u) != 0;
        HighlightColor? winner = null;
        int distinctColors = 0;

        for (int bit = 1; bit <= 31; bit++)
        {
            if ((mask & (1u << bit)) == 0) { continue; }

            if (bit - 1 >= tieHighlightFilters.Count) { return (null, "Mixed highlights"); }

            HighlightColor color = tieHighlightFilters[bit - 1].Color;

            if (color.ToCssName() is null)
            {
                hasUncolored = true;

                continue;
            }

            if (winner is null)
            {
                winner = color;
                distinctColors = 1;

                continue;
            }

            if (winner.Value != color)
            {
                distinctColors++;

                return (null, "Mixed highlights");
            }
        }

        if (hasUncolored && distinctColors > 0) { return (null, "Mixed highlights"); }

        return winner is { } highlight && distinctColors == 1
            ? (highlight.ToCssName(), $"{HighlightColorDisplayName(highlight)} highlight")
            : (null, "Uncolored");
    }

    protected override async ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
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

        ObserveSource(ActiveEventLog, OnActiveLogChangedAsync);
        ObserveSource(EventFocus, OnFocusChangedAsync);
        ObserveSource(Filters, OnFiltersChangedAsync);
        ObserveSource(DimensionRequest, OnDimensionRequestChangedAsync);

        Settings.TimeZoneChanged += OnTimeZoneChanged;
        FindMarkers.MarksChanged += OnFindMarksChanged;

        var initialRequest = DimensionRequest.Current;

        if (initialRequest is not null && initialRequest.Token > _appliedDimensionToken)
        {
            _dimension = initialRequest.Dimension;
            _appliedDimensionToken = initialRequest.Token;
        }

        RefreshFindTicks();
        RefreshTieFilters(rescanOnPredicateChange: false);

        base.OnInitialized();

        _lastScannedToken = Presentation.ContentToken;
    }

    protected override void OnPresentationChanged()
    {
        if (_lastScannedToken == Presentation.ContentToken) { return; }

        _lastScannedToken = Presentation.ContentToken;
        ScheduleRecompute();
    }

    private static string DimensionLabel(HistogramDimension dimension) => dimension switch
    {
        HistogramDimension.EventId => "Event ID",
        HistogramDimension.LogonType => "Logon Type",
        HistogramDimension.TaskCategory => "Task Category",
        HistogramDimension.TicketEncryptionType => "Ticket Encryption Type",
        HistogramDimension.ErrorCode => "Error Code",
        HistogramDimension.ProcessImage => "Process Image",
        HistogramDimension.ParentProcessImage => "Parent Process Image",
        _ => dimension.ToString()
    };

    private static string EmptyStateMessage(HistogramDimension dimension, bool visibleRange) => dimension switch
    {
        HistogramDimension.ErrorCode => visibleRange
            ? "No update error codes in the visible range."
            : "No update error codes in this view.",
        HistogramDimension.ProcessImage => visibleRange
            ? "No process image names in the visible range."
            : "No process image names in this view.",
        HistogramDimension.ParentProcessImage => visibleRange
            ? "No parent process image names in the visible range."
            : "No parent process image names in this view.",
        _ => visibleRange
            ? "No events to chart in the current view."
            : $"No {DimensionLabel(dimension)} values in this view."
    };

    private static string FindMarkerPoints(double centerX) =>
        $"{FormatCoordinate(centerX - 3)},0 {FormatCoordinate(centerX + 3)},0 {FormatCoordinate(centerX)},5";

    private static string FormatCoordinate(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string HighlightColorDisplayName(HighlightColor color)
    {
        string name = color.ToString();
        var parts = new List<string>();
        int start = 0;

        for (int index = 1; index < name.Length; index++)
        {
            if (!char.IsUpper(name[index])) { continue; }

            parts.Add(name[start..index]);
            start = index;
        }

        parts.Add(name[start..]);

        string joined = string.Join(" ", parts).ToLowerInvariant();

        return joined.Length == 0 ? joined : char.ToUpperInvariant(joined[0]) + joined[1..];
    }

    private static bool IsCategoricalOther(HistogramData data, int group) =>
        group < data.Groups.Count && data.Groups[group].ColorClass == "histogram-cat-other";

    private static bool ShouldArmTie(SavedFilter[] filters)
    {
        if (filters.Length is 0 or > 31) { return false; }

        foreach (SavedFilter filter in filters)
        {
            if (filter.Color != HighlightColor.None) { return true; }
        }

        return false;
    }

    private void AggregateAndRender(bool syncScrollbar = true)
    {
        _binCursor = null;

        if (_baseData is not { } data || data.GroupingFieldAbsent)
        {
            _render = null;

            if (_baseData is { GroupingFieldAbsent: true })
            {
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

    private string BarTooltip(HistogramRenderBin bin)
    {
        var start = ToDisplay(new DateTime(bin.StartTicks, DateTimeKind.Utc));
        var end = ToDisplay(new DateTime(Math.Max(bin.StartTicks, bin.EndTicks), DateTimeKind.Utc));
        bool crossesDay = WindowCrossesDay();
        string startText = crossesDay ? $"{start:d} {start:HH:mm:ss}" : $"{start:HH:mm:ss}";
        string endText = crossesDay ? $"{end:d} {end:HH:mm:ss}" : $"{end:HH:mm:ss}";

        return $"{bin.Total} {_baseData?.EventNoun ?? "events"}{GroupBreakdown(bin)}, {startText} - {endText}";
    }

    private int BarsAreaHeight() => Math.Max(0, _plotHeightPx - AxisReservePx);

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

        Span<bool> hidden = groupCount <= StackHiddenGroupThreshold ? stackalloc bool[StackHiddenGroupThreshold] : new bool[groupCount];
        hidden = hidden[..groupCount];

        for (int group = 0; group < groupCount; group++) { hidden[group] = IsGroupHidden(group); }

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

            if (count > 0)
            {
                string highlight = GroupHighlightText(group);
                parts.Add(string.IsNullOrEmpty(highlight)
                    ? $"{count} {data.Groups[group].Label}"
                    : $"{count} {data.Groups[group].Label}, {highlight}");
            }
        }

        return parts.Count == 0 ? string.Empty : $" ({string.Join(", ", parts)})";
    }

    private string GroupColorClass(int group)
    {
        if (_baseData is not { } data || group >= data.Groups.Count) { return string.Empty; }

        if (IsCategoricalOther(data, group)) { return "histogram-cat-other"; }

        return data.GroupHighlightMasks is not null ? "histogram-cat-hl" : data.Groups[group].ColorClass;
    }

    private string? GroupHighlightCssName(int group)
    {
        if (_baseData is not { } data || IsCategoricalOther(data, group) || data.GroupHighlightMasks is not { } masks || group >= masks.Length) { return null; }

        return ResolveGroupHighlight(masks[group]).CssName;
    }

    private string GroupHighlightText(int group)
    {
        if (_baseData is not { } data || IsCategoricalOther(data, group) || data.GroupHighlightMasks is not { } masks || group >= masks.Length) { return string.Empty; }

        return ResolveGroupHighlight(masks[group]).Description;
    }

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

    private Task OnActiveLogChangedAsync()
    {
        if (_disposed) { return Task.CompletedTask; }

        _isZoomed = false;
        _windowHistory.Clear();
        SupersedeQueuedNavigation();
        _baseData = null;
        _render = null;
        RefreshFindTicks();
        ScheduleRecompute();

        return Task.CompletedTask;
    }

    private Task OnDimensionRequestChangedAsync()
    {
        if (_disposed) { return Task.CompletedTask; }

        var current = DimensionRequest.Current;

        if (current is null || current.Token <= _appliedDimensionToken) { return Task.CompletedTask; }

        _appliedDimensionToken = current.Token;
        ApplyDimension(current.Dimension, force: true);

        return Task.CompletedTask;
    }

    private void OnDimensionSelected(HistogramDimension dimension)
    {
        ApplyDimension(dimension, force: false);
    }

    private Task OnFiltersChangedAsync()
    {
        if (_disposed) { return Task.CompletedTask; }

        RefreshTieFilters(rescanOnPredicateChange: true);

        return Task.CompletedTask;
    }

    private void OnFindMarksChanged(object? sender, EventArgs args) => _ = InvokeAsync(() =>
    {
        if (_disposed) { return; }

        RefreshFindTicks();
        StateHasChanged();
    });

    private Task OnFocusChangedAsync()
    {
        if (_disposed) { return Task.CompletedTask; }

        ResolveFocusedTicks();
        StateHasChanged();

        return Task.CompletedTask;
    }

    private void OnTimeZoneChanged(object? sender, TimeZoneInfo timeZone) => _ = InvokeAsync(() =>
    {
        if (_disposed) { return; }

        _timeZone = timeZone;

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

    private void RecomputeSegmentHeights()
    {
        if (_render is { } render && _baseData is { } data) { ComputeSegmentHeights(render, data.Groups.Count); }
    }

    private void RefreshBinAnnouncement() =>
        _binAnnouncement = _binCursor is { } cursor && _render is { } render && cursor < render.Bins.Count ?
                BinCursorAnnouncement(render.Bins[cursor]) :
                string.Empty;

    private void RefreshFindTicks() =>
        _findTicks = FindMarkers.Owner == ActiveEventLog.Current && FindMarkers.Ticks is { Count: > 0 } ticks ? [.. ticks] : [];

    private void RefreshTieFilters(bool rescanOnPredicateChange)
    {
        ImmutableList<SavedFilter> filters = Filters.Current;
        SavedFilter[] selected = HighlightSelector.Select(filters);
        int planKey = HighlightSelector.ComputePredicatePlanKey(filters);

        bool wasArmed = ShouldArmTie(_tieHighlightFilters);
        bool willArm = ShouldArmTie(selected);
        bool rescanNeeded = wasArmed != willArm || (planKey != _tiePlanKey && (wasArmed || willArm));

        _tieHighlightFilters = selected;
        _tiePlanKey = planKey;

        if (rescanNeeded && rescanOnPredicateChange)
        {
            if (_baseData is { GroupHighlightMasks: not null } data)
            {
                _baseData = data with { GroupHighlightMasks = null };
            }

            RefreshBinAnnouncement();
            StateHasChanged();

            StartScan();

            return;
        }

        RefreshBinAnnouncement();

        StateHasChanged();
    }

    private string RegionAria() =>
        _baseData is { } data ? HistogramSummary.RegionLabel(data, _timeZone) : "Timeline";

    private void ResolveFocusedTicks()
    {
        IEventColumnView view = ViewSource.Current.View;

        _focusedTicks =
            EventFocus.Current?.CurrentHandle is { } handle &&
            view.Rank(handle) >= 0 &&
            view.TryGetTimeTicks(handle, out long ticks) ? ticks : null;
    }

    private (string? CssName, string Description) ResolveGroupHighlight(uint mask) =>
        ResolveGroupHighlight(mask, _tieHighlightFilters);

    private async Task RunScanAsync(
        IEventColumnView view,
        EventLogId? scannedTabId,
        HistogramDimension dimension,
        int scanVersion,
        SavedFilter[] tieHighlightFilters,
        int tiePlanKey,
        bool useHighlightTie,
        CancellationToken token)
    {
        HistogramData? data;

        try
        {
            data = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();

                if (useHighlightTie)
                {
                    byte[] highlightWinners = view.EnsureHighlightWinners(tieHighlightFilters, tiePlanKey, token);

                    return HistogramBuilder.BuildWithHighlightTie(view, dimension, HistogramConstants.MaxBuckets, highlightWinners, token);
                }

                return HistogramBuilder.Build(view, dimension, HistogramConstants.MaxBuckets, token);
            }, token);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e)
        {
            TraceLogger.Error($"{nameof(HistogramPane)}: histogram scan failed: {e}");

            // Marshal the failure back rather than only logging it. Without this the pane keeps whatever state the scan
            // began in and never assigns _baseData, so the empty-state branch reports "no events to chart" - a
            // statement about the user's data, for a chart that failed to build from it.
            try
            {
                await InvokeAsync(() =>
                {
                    // A superseded scan's failure says nothing about the request that replaced it.
                    if (_disposed || scanVersion != _scanVersion) { return; }

                    _chartState = HistogramChartState.Failed;

                    // Discard the chart rather than merely stop drawing it. Every interaction path reads these fields
                    // directly, not the rendered markup, so bars left behind stay clickable and drag-selectable and
                    // would issue a time-range filter derived from a scan that failed - acting on data rather than
                    // only misreading it. Nothing re-stamps a retained chart the way the grid's rows are re-stamped,
                    // so there is no version of these bars that stays true.
                    _baseData = null;
                    _render = null;
                    _binCursor = null;
                    _binAnnouncement = string.Empty;

                    // Retires any summary already queued behind the delay, which would otherwise describe the chart
                    // that just failed as though it had arrived.
                    _announceGeneration++;

                    StateHasChanged();
                });
            }
            catch (ObjectDisposedException) { /* Component torn down mid-failure; nothing to report to. */ }

            return;
        }

        try
        {
            await InvokeAsync(() =>
            {
                if (_disposed ||
                    token.IsCancellationRequested ||
                    scanVersion != _scanVersion ||
                    scannedTabId != ViewSource.Current.ActiveTabId) { return; }

                if (useHighlightTie && tiePlanKey != _tiePlanKey) { return; }

                _baseData = data;
                _chartState = HistogramChartState.Ready;
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
        if (_disposed) { return; }

        _scanVersion++;
        _chartState = HistogramChartState.Pending;

        if (_recomputePending) { return; }

        _recomputePending = true;

        _ = ThrottleThenScanAsync();
    }

    private void ScopeBin(HistogramRenderBin bin) =>
        FilterLensCommands.ShowTimeRange(
            new DateTime(bin.StartTicks, DateTimeKind.Utc),
            new DateTime(bin.EndTicks, DateTimeKind.Utc),
            _timeZone,
            Presentation.ActiveLogName);

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
            Presentation.ActiveLogName);
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

    private void SetWindowByBins(HistogramData data, int startBin, int binCount, bool recordHistory = false)
    {
        int totalBins = data.BinCount;
        int minBins = Math.Min(MinWindowBaseBins, totalBins);

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

        int scanVersion = ++_scanVersion;

        _chartState = HistogramChartState.Pending;

        if (_viewportWidthPx <= 0 || _plotHeightPx <= 0) { return; }

        var current = ViewSource.Current;
        IEventColumnView view = current.View;
        EventLogId? scannedTabId = current.ActiveTabId;
        HistogramDimension dimension = _dimension;
        SavedFilter[] tieHighlightFilters = _tieHighlightFilters;
        int tiePlanKey = _tiePlanKey;
        bool useHighlightTie = ShouldArmTie(tieHighlightFilters);
        var cts = new CancellationTokenSource();
        _scanCts = cts;

        _ = RunScanAsync(view, scannedTabId, dimension, scanVersion, tieHighlightFilters, tiePlanKey, useHighlightTie, cts.Token);
    }

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

    private void ZoomFromControl(double factor)
    {
        SupersedeQueuedNavigation();
        ApplyZoom(factor, 0.5);
    }

    private readonly record struct AxisLabel(double X, string Text, string Anchor);
}
