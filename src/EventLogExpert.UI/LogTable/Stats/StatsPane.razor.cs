// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using EventLogExpert.Runtime.Stats;
using EventLogExpert.UI.Common.Interop;
using EventLogExpert.UI.Modal;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Globalization;
using System.Text;

namespace EventLogExpert.UI.LogTable.Stats;

public sealed partial class StatsPane
{
    private const int DefaultTopN = 8;
    private const int HeadlineTopSourceCount = 3;
    private const int MaxFitRows = 40;
    private const int MinFitRows = 1;
    private const int RecomputeThrottleMs = 500;
    private const int RowHeightPx = 22;
    // One section's non-row chrome: its header + coverage line + one row reserved for the pinned (none) footer. The
    // resize observer reports a single section's height (not the whole pane), so this fit stays correct across the
    // 1/2/4-column responsive layouts, and the reserved row keeps the pinned (none) bucket from ever being clipped.
    private const int SectionChromePx = 68;

    // Severity display order (most severe first) with the Unknown catch-all last. Slot indices come from
    // SeverityLevel (0 = Unknown, 1-5 = Critical..Verbose), matching LevelSeverity.Slot.
    private static readonly SeveritySlot[] s_severityOrder =
    [
        new((int)SeverityLevel.Critical, nameof(SeverityLevel.Critical), "stats-sev-critical"),
        new((int)SeverityLevel.Error, nameof(SeverityLevel.Error), "stats-sev-error"),
        new((int)SeverityLevel.Warning, nameof(SeverityLevel.Warning), "stats-sev-warning"),
        new((int)SeverityLevel.Information, nameof(SeverityLevel.Information), "stats-sev-information"),
        new((int)SeverityLevel.Verbose, nameof(SeverityLevel.Verbose), "stats-sev-verbose"),
        new(0, "Unknown", "stats-sev-unknown")
    ];

    private static int s_resizeSession;

    private readonly CancellationTokenSource _lifetimeCts = new();

    private readonly List<DimensionSection> _sections =
    [
        new(StatsDimension.Source),
        new(StatsDimension.EventId),
        new(StatsDimension.TaskCategory),
        new(StatsDimension.User)
    ];

    private ViewContentToken _currentToken = ViewContentToken.Empty;
    private bool _disposed;
    private bool _fitDirty;
    private int _fitTopN = DefaultTopN;
    private EventLogId? _lastScannedTab;
    private ViewContentToken? _lastScannedToken;
    private ElementReference _paneElement;
    private bool _recomputePending;
    private IJSObjectReference? _resizeModule;
    private int _resizeSession;
    private CancellationTokenSource? _scanCts;
    private bool _scanFailed;
    private int _scanVersion;
    private DotNetObjectReference<StatsPane>? _selfRef;
    private SeverityStats? _severity;
    private ViewContentToken _severityToken = ViewContentToken.Empty;

    [Inject] private IFilterLensCommands FilterLensCommands { get; init; } = null!;

    [Inject] private IFilterLensSource FilterLenses { get; init; } = null!;

    private bool IsSeverityStale => _severityToken != _currentToken;

    [Inject] private IJSRuntime JsRuntime { get; init; } = null!;

    [Inject] private IModalCoordinator ModalCoordinator { get; init; } = null!;

    [Inject] private IStatsService StatsService { get; init; } = null!;

    [Inject] private ITraceLogger TraceLogger { get; init; } = null!;

    // Called from the resize observer with one section's height: fit the row count to a single column so every column
    // fills its cell without a scrollbar. A fixed row height keeps this a pure arithmetic step (no layout thrash).
    [JSInvokable]
    public Task OnStatsResized(int height)
    {
        if (_disposed) { return Task.CompletedTask; }

        int fit = Math.Clamp((height - SectionChromePx) / RowHeightPx, MinFitRows, MaxFitRows);

        if (fit != _fitTopN)
        {
            _fitTopN = fit;
            StartScan();
        }

        return Task.CompletedTask;
    }

    protected override async ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            _lifetimeCts.Cancel();
            _lifetimeCts.Dispose();

            try { _scanCts?.Cancel(); } catch (ObjectDisposedException) { /* Already disposed; cancel is moot. */ }

            _scanCts?.Dispose();
            _scanCts = null;

            await JsModuleInterop.DisposeModuleSafelyAsync(
                _resizeModule,
                module => module.InvokeVoidAsync("disposeStatsResize", _resizeSession));

            _resizeModule = null;
            _selfRef?.Dispose();
            _selfRef = null;
        }

        await base.DisposeAsyncCore(disposing);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && !_disposed)
        {
            try
            {
                _resizeModule = await JsRuntime.InvokeAsync<IJSObjectReference>(
                    "import", "./_content/EventLogExpert.UI/LogTable/Stats/StatsPane.razor.js");

                // The import can outlive a fast close/reopen. A pane disposed mid-import must NOT touch the shared
                // observer state: it releases only its own module handle. Session-scoped ownership means the live pane
                // keeps its observer, so calling disposeStatsResize here is deliberately avoided.
                if (_disposed)
                {
                    try { await _resizeModule.DisposeAsync(); }
                    catch (JSDisconnectedException) { /* Circuit already gone. */ }
                    catch (ObjectDisposedException) { /* Reference already released. */ }

                    _resizeModule = null;
                    return;
                }

                _selfRef = DotNetObjectReference.Create(this);

                // Own the session token BEFORE the interop so a disposal racing this init still tears down the exact
                // observer initStatsResize is about to create - DisposeAsyncCore reads _resizeSession rather than a
                // still-pending return value.
                _resizeSession = Interlocked.Increment(ref s_resizeSession);
                await _resizeModule.InvokeVoidAsync("initStatsResize", _resizeSession, _selfRef, _paneElement);
            }
            catch (JSDisconnectedException) { /* Circuit closed before the observer attached; nothing to observe. */ }
            catch (ObjectDisposedException) { /* Component torn down mid-init. */ }
        }

        // Sections render only after a scan publishes, and their appearance does not resize the pane box, so the
        // observer would not re-fire to correct the initial fit. Nudge one re-measure after such a render.
        if (_fitDirty && !_disposed && _resizeModule is { } module)
        {
            _fitDirty = false;

            try { await module.InvokeVoidAsync("remeasureStatsResize", _resizeSession); }
            catch (JSDisconnectedException) { /* Circuit gone; the next resize re-fits. */ }
            catch (ObjectDisposedException) { /* Torn down; nothing to re-fit. */ }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        _lastScannedTab = Presentation.ActiveTabId;
        _lastScannedToken = Presentation.ContentToken;
        _currentToken = Presentation.ContentToken;

        StartScan();
    }

    protected override void OnPresentationChanged()
    {
        // The publish guard also enforces ActiveTabId, so the recompute key must include it: a tab switch that keeps
        // the same content token (e.g. between two empty tabs) would otherwise reject the in-flight scan on tab
        // mismatch while scheduling no replacement, leaving the pane stuck.
        if (_lastScannedTab == Presentation.ActiveTabId && _lastScannedToken == Presentation.ContentToken) { return; }

        _lastScannedTab = Presentation.ActiveTabId;
        _lastScannedToken = Presentation.ContentToken;
        _currentToken = Presentation.ContentToken;
        ScheduleRecompute();
    }

    private static string CoverageNoun(StatsDimension dimension, int count)
    {
        bool singular = count == 1;

        return dimension switch
        {
            StatsDimension.Source => singular ? "source" : "sources",
            StatsDimension.EventId => singular ? "event ID" : "event IDs",
            StatsDimension.TaskCategory => singular ? "task category" : "task categories",
            StatsDimension.User => singular ? "user" : "users",
            _ => singular ? "value" : "values"
        };
    }

    private static string CoverageText(DimensionStats stats)
    {
        string noun = CoverageNoun(stats.Dimension, stats.DistinctCount);
        int coveredPercent = (int)Math.Round(Share(stats.ShownEventCount, stats.Total), MidpointRounding.AwayFromZero);

        string scope = stats.Top.Count >= stats.DistinctCount
            ? $"All {FormatCount(stats.DistinctCount)} {noun}"
            : $"Top {stats.Top.Count} of {FormatCount(stats.DistinctCount)} {noun}";

        return $"{scope} - {coveredPercent}% of events";
    }

    private static string Css(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string FormatCount(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private static string FormatPercent(double percent) =>
        (percent >= 9.95 ? percent.ToString("0", CultureInfo.CurrentCulture) : percent.ToString("0.0", CultureInfo.CurrentCulture)) + "%";

    private static double Share(int count, int total) => total == 0 ? 0 : count * 100.0 / total;

    private void ApplyRowLens(DimensionSection section, StatsContributor contributor, bool include)
    {
        // The row reflects a scan that may have been superseded by a newer view; filtering on a value that is no longer
        // present would push a lens that matches nothing. Refuse and let the pending recompute redraw the live rows.
        if (IsSectionStale(section))
        {
            ScheduleRecompute();
            return;
        }

        section.Dimension.PushRowFilter(FilterLensCommands, contributor.Value, Presentation.ActiveLogName, include);
    }

    private bool AreRowActionsDisabled(DimensionSection section) => section.Stats is null || IsSectionStale(section);

    private void ExcludeContributor(DimensionSection section, StatsContributor contributor) =>
        ApplyRowLens(section, contributor, include: false);

    private string HeadlineText()
    {
        if (_severity is not var (total, slots)) { return "Computing statistics..."; }

        if (total == 0)
        {
            return FilterLenses.Lenses.Count > 0 ?
                "0 events - remove a lens to continue" :
                "No events in the current view";
        }

        var headline = new StringBuilder();
        headline.Append(FormatCount(total)).Append(total == 1 ? " event" : " events");

        int errorCritical = slots[(int)SeverityLevel.Critical] + slots[(int)SeverityLevel.Error];

        if (errorCritical > 0)
        {
            headline.Append(" - ").Append(FormatCount(errorCritical)).Append(" error/critical");
        }

        DimensionSection source = SectionFor(StatsDimension.Source);

        if (!IsSectionStale(source) && source.Stats is { Top.Count: > 0 } sourceStats)
        {
            int shown = Math.Min(HeadlineTopSourceCount, sourceStats.Top.Count);
            int covered = 0;

            for (int index = 0; index < shown; index++) { covered += sourceStats.Top[index].Count; }

            int percent = (int)Math.Round(covered * 100.0 / total, MidpointRounding.AwayFromZero);

            headline.Append(" - top ")
                .Append(shown)
                .Append(shown == 1 ? " source = " : " sources = ")
                .Append(percent)
                .Append('%');
        }

        return headline.ToString();
    }

    private void IncludeContributor(DimensionSection section, StatsContributor contributor) =>
        ApplyRowLens(section, contributor, include: true);

    private bool IsSectionStale(DimensionSection section) => section.StatsToken != _currentToken;

    private async Task MarkFailedAsync(int scanVersion, EventLogId? tab, ViewContentToken contentToken)
    {
        try
        {
            await InvokeAsync(() =>
            {
                if (_disposed || scanVersion != _scanVersion) { return; }

                OrderedViewPresentation current = ViewSource.Current;

                // A superseded or wrong-tab scan's failure says nothing about the request that replaced it.
                if (tab != current.ActiveTabId || contentToken != current.ContentToken) { return; }

                _scanFailed = true;
                StateHasChanged();
            });
        }
        catch (ObjectDisposedException) { /* Component torn down mid-failure; nothing to report. */ }
    }

    private void OpenDetail(StatsDimension dimension) =>
        _ = ModalCoordinator.OpenStatsDetailAsync(dimension, ViewSource.Current.View, Presentation.ActiveLogName);

    private async Task<bool> PublishAsync(int scanVersion, EventLogId? tab, ViewContentToken contentToken, Action apply)
    {
        bool applied = false;

        try
        {
            await InvokeAsync(() =>
            {
                if (_disposed || scanVersion != _scanVersion) { return; }

                OrderedViewPresentation current = ViewSource.Current;

                // A scan is only true for the exact tab and content it ran against; a presentation swap mid-scan
                // retires the result rather than stamping stale numbers onto the live view.
                if (tab != current.ActiveTabId || contentToken != current.ContentToken) { return; }

                apply();
                applied = true;
                StateHasChanged();
            });
        }
        catch (ObjectDisposedException) { return false; }

        return applied;
    }

    private async Task RunScanAsync(
        IEventColumnView view,
        EventLogId? tab,
        ViewContentToken contentToken,
        int scanVersion,
        (StatsDimension Dimension, int TopN)[] requests,
        CancellationToken cancellationToken)
    {
        try
        {
            SeverityStats severity =
                await Task.Run(() => StatsService.BuildSeverity(view, cancellationToken), cancellationToken);

            if (!await PublishAsync(scanVersion, tab, contentToken, () =>
                {
                    _severity = severity;
                    _severityToken = contentToken;
                    _fitDirty = true;
                }))
            {
                return;
            }

            foreach ((StatsDimension dimension, int topN) in requests)
            {
                DimensionStats stats = await Task.Run(
                    () => StatsService.BuildDimension(view, dimension, topN, cancellationToken),
                    cancellationToken);

                if (!await PublishAsync(scanVersion, tab, contentToken, () =>
                    {
                        DimensionSection section = SectionFor(dimension);
                        section.Stats = stats;
                        section.StatsToken = contentToken;
                    }))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) { /* Superseded or torn down; the replacement scan owns the result. */ }
        catch (Exception scanFailure)
        {
            TraceLogger.Error($"{nameof(StatsPane)}: statistics scan failed: {scanFailure}");
            await MarkFailedAsync(scanVersion, tab, contentToken);
        }
    }

    private void ScheduleRecompute()
    {
        if (_disposed) { return; }

        _scanVersion++;

        if (_recomputePending) { return; }

        _recomputePending = true;
        _ = ThrottleThenScanAsync();
    }

    private DimensionSection SectionFor(StatsDimension dimension) =>
        _sections.First(section => section.Dimension == dimension);

    private string SeverityBarAria() =>
        "Severity breakdown: " +
        string.Join(", ", SeveritySegments().Select(segment => $"{FormatCount(segment.Count)} {segment.Label.ToLower(CultureInfo.CurrentCulture)}"));

    private IReadOnlyList<SeveritySegment> SeveritySegments()
    {
        if (_severity is not { Total: > 0 } severity) { return []; }

        var segments = new List<SeveritySegment>(s_severityOrder.Length);

        foreach (SeveritySlot slot in s_severityOrder)
        {
            int count = severity.Slots[slot.Index];

            if (count == 0) { continue; }

            segments.Add(new SeveritySegment(slot.Label, slot.CssClass, count, Share(count, severity.Total)));
        }

        return segments;
    }

    private void StartScan()
    {
        if (_disposed) { return; }

        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = null;

        int scanVersion = ++_scanVersion;

        OrderedViewPresentation current = ViewSource.Current;
        IEventColumnView view = current.View;
        EventLogId? tab = current.ActiveTabId;
        ViewContentToken contentToken = current.ContentToken;
        _currentToken = contentToken;
        _scanFailed = false;

        // Every scan recomputes severity plus all four dimension columns; each column shows exactly the number of rows
        // that fit the drawer height (no scrollbar), so the full list is reached by resizing the drawer or "View all".
        (StatsDimension Dimension, int TopN)[] requests =
        [
            .. _sections
                .Select(section => (section.Dimension, TopN: _fitTopN))
        ];

        var cts = new CancellationTokenSource();
        _scanCts = cts;

        _ = RunScanAsync(view, tab, contentToken, scanVersion, requests, cts.Token);
    }

    private async Task ThrottleThenScanAsync()
    {
        try { await Task.Delay(RecomputeThrottleMs, _lifetimeCts.Token); }
        catch (OperationCanceledException) { return; }

        _recomputePending = false;
        StartScan();
    }

    private sealed class DimensionSection(StatsDimension dimension)
    {
        public StatsDimension Dimension { get; } = dimension;

        public DimensionStats? Stats { get; set; }

        public ViewContentToken StatsToken { get; set; } = ViewContentToken.Empty;
    }

    private sealed record SeveritySlot(int Index, string Label, string CssClass);

    private sealed record SeveritySegment(string Label, string CssClass, int Count, double Percent);
}
