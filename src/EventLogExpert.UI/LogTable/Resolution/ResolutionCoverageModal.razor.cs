// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Concurrency;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.ResolutionCoverage;
using EventLogExpert.UI.Modal;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.LogTable.Resolution;

public sealed partial class ResolutionCoverageModal : ModalBase<bool>
{
    // The display cap only guards a pathological log with thousands of distinct sources; sorting happens before it.
    private const int MaxProviders = 500;

    private static readonly TimeSpan s_copiedFeedbackDuration = TimeSpan.FromSeconds(2);

    private readonly CancellationTokenSource _cts = new();

    private readonly Dictionary<string, ProviderCoverageDetail> _detailCache = new(StringComparer.Ordinal);
    private CancellationTokenSource? _copiedCts;
    private bool _copying;
    private ProviderCoverageDetail? _detail;
    private CancellationTokenSource? _detailCts;
    private bool _detailFailed;
    private int _detailGeneration;
    private bool _detailLoading;
    private string? _expandedProvider;
    private bool _failed;
    private string? _faultCause;
    private bool _isFiltered;
    private bool _loading = true;
    private string? _originLog;
    private ResolutionCoverageReport? _report;
    private string _search = string.Empty;
    private bool _showCopied;
    private CoverageSortColumn _sortColumn = CoverageSortColumn.Unresolved;
    private bool _sortDescending = true;
    private bool _updating;
    private IEventColumnView? _view;

    private enum CoverageSortColumn { Unresolved, Provider, Total, Resolved, NoProvider, NoMessage, Failed, Coverage }

    private enum ResolutionCause { NoProvider, NoMessage, Failed }

    [Inject] private IClipboardService ClipboardService { get; init; } = null!;

    [Inject] private IResolutionCoverageService CoverageService { get; init; } = null!;

    [Inject] private ICpuWorkScheduler CpuScheduler { get; init; } = null!;

    [Inject] private IFilterAppliedSource FilterApplied { get; init; } = null!;

    [Inject] private IFilterLensCommands FilterLensCommands { get; init; } = null!;

    // Gates the footer action fragment, which lives OUTSIDE the body's _report render chain: the actions only make
    // sense once a non-empty report is shown (mirrors the body's terminal branch), so they stay hidden while the modal
    // is loading, updating, faulted, or empty.
    private bool HasReport => !_loading && !_updating && !_failed && _report is { Summary.Total: > 0 };

    private int Unresolved => _report?.Summary.Unresolved ?? 0;

    [Inject] private IOrderedViewSource ViewSource { get; init; } = null!;

    protected override ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing)
        {
            _cts.Cancel();
            _cts.Dispose();

            CancellationTokenSource? copiedCts = _copiedCts;
            _copiedCts = null;
            copiedCts?.Cancel();
            copiedCts?.Dispose();

            CancellationTokenSource? detailCts = _detailCts;
            _detailCts = null;
            detailCts?.Cancel();
            detailCts?.Dispose();

            _view = null;
            _detailCache.Clear();
        }

        return base.DisposeAsyncCore(disposing);
    }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();

        OrderedViewPresentation presentation = ViewSource.Current;
        _isFiltered = FilterApplied.IsFilteringEnabled;
        _originLog = presentation.ActiveLogName;

        switch (presentation.IndicatorKind)
        {
            case DisplayIndicatorKind.Fault:
                _faultCause = presentation.FaultCause;
                _failed = true;
                _loading = false;
                return;
            case DisplayIndicatorKind.EmptyPending:
                _updating = true;
                _loading = false;
                return;
        }

        _view = presentation.View;

        try
        {
            IEventColumnView view = _view;
            // UserInitiated: user opened this modal and is watching its spinner. Do NOT add ConfigureAwait(false) - the
            // continuation mutates _report and calls StateHasChanged() without InvokeAsync, so it must resume on the Blazor dispatcher.
            _report = await CpuScheduler.RunAsync(reportToken => CoverageService.Build(view, reportToken), CpuWorkPriority.UserInitiated, _cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            _failed = true;
        }
        finally
        {
            if (!IsDisposed)
            {
                _loading = false;
                StateHasChanged();
            }
        }
    }

    private static string CoverageCssClass(CoverageStatus status) => status switch
    {
        CoverageStatus.Full => "coverage-pill coverage-full",
        CoverageStatus.None => "coverage-pill coverage-none",
        _ => "coverage-pill coverage-partial"
    };

    private static string? CoverageTooltip(ProviderCoverageRow row)
    {
        if (row.Status == CoverageStatus.Full) { return null; }

        if (row.Status == CoverageStatus.None)
        {
            return "No provider metadata for any of this provider's events - readable fallback text may still be shown.";
        }

        var causes = new List<string>(3);

        if (row.Counts.NoProvider > 0) { causes.Add("no provider metadata"); }
        if (row.Counts.NoMessage > 0) { causes.Add("no message match"); }
        if (row.Counts.Failed > 0) { causes.Add("a resolution error"); }

        return $"Some events could not be fully resolved: {string.Join(", ", causes)}.";
    }

    private static string DatabaseActionTooltip(ProviderCoverageRow row) => row.Counts.NoProvider > 0 ?
        $"Loading a provider database may add descriptions for {row.Provider}." :
        $"Loading a newer provider database may add message definitions for {row.Provider}.";

    private static string DetailId(int index) => $"coverage-detail-{index}";

    private static ResolutionCause DominantCause(ProviderResolutionCounts counts)
    {
        // Tie precedence NoProvider > NoMessage > Failed, matching the database-action gate and CoverageStatus.Classify.
        if (counts.NoProvider >= counts.NoMessage && counts.NoProvider >= counts.Failed) { return ResolutionCause.NoProvider; }

        return counts.NoMessage >= counts.Failed ? ResolutionCause.NoMessage : ResolutionCause.Failed;
    }

    private static string DominantCauseLabel(ProviderResolutionCounts counts) => DominantCause(counts) switch
    {
        ResolutionCause.NoProvider => ResolutionStatusTokens.NoProvider,
        ResolutionCause.NoMessage => ResolutionStatusTokens.NoMessage,
        _ => ResolutionStatusTokens.Failed
    };

    private static string FormatCount(int value) => TallyFormatter.Count(value);

    private static string SegmentWidth(double percent) => FormattableString.Invariant($"width:{percent:0.###}%;");

    private static string SeverityBarLabel(ProviderCoverageDetail detail) =>
        "Unresolved by severity: " +
        string.Join(", ", detail.Levels.Select(level => $"{SeverityDisplay.Label(level.Level)} {level.Counts.Unresolved}"));

    private static IEnumerable<(SeverityLevel? Level, int Unresolved, double Percent)> SeveritySegments(ProviderCoverageDetail detail)
    {
        int total = detail.Levels.Sum(level => level.Counts.Unresolved);

        if (total == 0) { yield break; }

        foreach (LevelCoverageRow level in detail.Levels)
        {
            yield return (level.Level, level.Counts.Unresolved, level.Counts.Unresolved * 100.0 / total);
        }
    }

    private static bool ShowDatabaseAction(ProviderCoverageRow row) =>
        row.Counts.NoProvider > 0 || row.Counts.NoMessage > 0;

    private string AriaSort(CoverageSortColumn column) =>
        _sortColumn != column ? "none" : _sortDescending ? "descending" : "ascending";

    private void CancelDetailScan()
    {
        CancellationTokenSource? scan = _detailCts;
        _detailCts = null;
        scan?.Cancel();
        scan?.Dispose();
    }

    private IEnumerable<(string Token, int Count)> CauseBreakdown()
    {
        if (_report is null) { yield break; }

        yield return (ResolutionStatusTokens.NoProvider, _report.Summary.NoProvider);
        yield return (ResolutionStatusTokens.NoMessage, _report.Summary.NoMessage);
        yield return (ResolutionStatusTokens.Failed, _report.Summary.Failed);
    }

    private void CollapseDetail()
    {
        CancelDetailScan();
        _expandedProvider = null;
        _detail = null;
        _detailLoading = false;
        _detailFailed = false;

        // Invalidate any in-flight scan so a late completion cannot repopulate a collapsed row.
        ++_detailGeneration;
    }

    private async Task CopyTableAsync()
    {
        // The synchronous formatter cannot be canceled mid-build, so the guard - not a superseding CTS - is what
        // prevents concurrent large allocations from rapid clicks.
        if (_copying || _report is null) { return; }

        _copying = true;

        try
        {
            ResolutionCoverageReport report = _report;
            bool isFiltered = _isFiltered;
            string tsv = await Task.Run(() => CoverageTableFormatter.Format(report, isFiltered), _cts.Token);
            await ClipboardService.CopyTextAsync(tsv);

            if (IsDisposed) { return; }

            _ = ShowCopiedFeedbackAsync();
        }
        catch (OperationCanceledException) { }
        finally
        {
            _copying = false;
        }
    }

    private async Task ExcludeProviderAsync(ProviderCoverageRow row)
    {
        FilterLensCommands.ExcludeValue(EventProperty.Source, row.Provider, _originLog);
        await CompleteAsync(true);
    }

    private async Task FilterByCauseAsync(string token)
    {
        FilterLensCommands.IncludeValue(EventProperty.ResolutionStatus, token, _originLog);
        await CompleteAsync(true);
    }

    private async Task FilterEventIdUnresolvedAsync(string provider, int eventId)
    {
        if (string.IsNullOrWhiteSpace(provider)) { return; }

        FilterLensCommands.IncludeValue(EventProperty.Source, provider, _originLog);
        FilterLensCommands.IncludeEventId(eventId, _originLog);
        FilterLensCommands.ExcludeValue(EventProperty.ResolutionStatus, ResolutionStatusTokens.Resolved, _originLog);
        await CompleteAsync(true);
    }

    private async Task FilterProviderUnresolvedAsync(ProviderCoverageRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Provider) || row.Status == CoverageStatus.Full) { return; }

        // Include the provider first, then exclude resolved events, so the view narrows to exactly this provider's
        // unresolved events (both lenses remain independently removable afterward).
        FilterLensCommands.IncludeValue(EventProperty.Source, row.Provider, _originLog);
        FilterLensCommands.ExcludeValue(EventProperty.ResolutionStatus, ResolutionStatusTokens.Resolved, _originLog);
        await CompleteAsync(true);
    }

    private List<ProviderCoverageRow> FilteredSortedRows()
    {
        if (_report is null) { return []; }

        IEnumerable<ProviderCoverageRow> rows = _report.Rows;

        if (!string.IsNullOrWhiteSpace(_search))
        {
            string needle = _search.Trim();
            rows = rows.Where(row => row.Provider.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        return [.. SortRows(rows)];
    }

    private string FormatShare(int count) => TallyFormatter.Share(count, _report?.Summary.Total ?? 0);

    private async Task IncludeProviderAsync(ProviderCoverageRow row)
    {
        FilterLensCommands.IncludeValue(EventProperty.Source, row.Provider, _originLog);
        await CompleteAsync(true);
    }

    private bool IsExpanded(ProviderCoverageRow row) =>
        string.Equals(_expandedProvider, row.Provider, StringComparison.Ordinal);

    private async Task LoadDetailAsync(ProviderCoverageRow row)
    {
        // Supersede any in-flight scan; cancel the LINKED cts, never the shared _cts the copy path also uses.
        CancelDetailScan();

        _detailFailed = false;
        int generation = ++_detailGeneration;

        if (_detailCache.TryGetValue(row.Provider, out ProviderCoverageDetail? cached))
        {
            _detail = cached;
            _detailLoading = false;

            return;
        }

        _detail = null;

        if (_view is not { } view)
        {
            _detailLoading = false;
            _detailFailed = true;

            return;
        }

        _detailLoading = true;

        CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        _detailCts = linked;
        string provider = row.Provider;

        ProviderCoverageDetail? detail = null;
        bool failed = false;

        try
        {
            // UserInitiated: user-driven provider expand. Do NOT add ConfigureAwait(false) - the continuation mutates
            // detail state and renders without InvokeAsync, so it must resume on the Blazor dispatcher.
            detail = await CpuScheduler.RunAsync(detailToken => CoverageService.BuildProviderDetail(view, provider, detailToken), CpuWorkPriority.UserInitiated, linked.Token);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception) { failed = true; }

        // Ignore a completion that was superseded by another expand/collapse or that arrived after disposal.
        if (IsDisposed || generation != _detailGeneration) { return; }

        _detailLoading = false;

        if (failed)
        {
            _detailFailed = true;
        }
        else
        {
            _detailCache[provider] = detail!;
            _detail = detail;
        }

        StateHasChanged();
    }

    private async Task OpenDatabaseToolsAsync()
    {
        await CompleteAsync(true);
        await ModalCoordinator.OpenDatabaseToolsAsync();
    }

    private string ProportionAriaLabel()
    {
        if (_report is null) { return string.Empty; }

        ProviderResolutionCounts summary = _report.Summary;

        return $"Resolution mix: {FormatShare(summary.Resolved)} resolved, " +
            $"{FormatShare(summary.NoProvider)} no provider metadata, " +
            $"{FormatShare(summary.NoMessage)} no message match, " +
            $"{FormatShare(summary.Failed)} resolution error.";
    }

    private IEnumerable<(string Label, string CssClass, double Percent)> ProportionSegments()
    {
        if (_report is null || _report.Summary.Total == 0) { yield break; }

        ProviderResolutionCounts summary = _report.Summary;
        double total = summary.Total;

        if (summary.Resolved > 0) { yield return ("Resolved", "coverage-seg-resolved", summary.Resolved * 100.0 / total); }
        if (summary.NoProvider > 0) { yield return (ResolutionStatusTokens.NoProvider, "coverage-seg-noprovider", summary.NoProvider * 100.0 / total); }
        if (summary.NoMessage > 0) { yield return (ResolutionStatusTokens.NoMessage, "coverage-seg-nomessage", summary.NoMessage * 100.0 / total); }
        if (summary.Failed > 0) { yield return (ResolutionStatusTokens.Failed, "coverage-seg-failed", summary.Failed * 100.0 / total); }
    }

    private string RemediationHint(ProviderCoverageRow row) => DominantCause(row.Counts) switch
    {
        ResolutionCause.NoProvider =>
            $"No provider metadata is installed for {row.Provider} on this machine. Import a provider database from Database Tools, or open this log where the source software is installed.",
        ResolutionCause.NoMessage =>
            $"{row.Provider} has provider metadata but no message definition for these event IDs. A newer provider database may add them.",
        _ =>
            $"Resolution hit an unexpected error for some of {row.Provider}'s events. Reopening the log, or inspecting the raw event XML, may help."
    };

    // Re-runs the scan for the already-expanded provider (the failure-state Retry) without collapsing first, so Retry
    // cannot land on ToggleExpandAsync's collapse branch.
    private Task RetryDetailAsync(ProviderCoverageRow row) => LoadDetailAsync(row);

    // Mirrors the CriticalBanner feedback timer: only the newest cycle clears the flag, and disposal (which nulls
    // _copiedCts and cancels the token) makes the ReferenceEquals guard fail so StateHasChanged never runs afterward.
    private async Task ShowCopiedFeedbackAsync()
    {
        if (IsDisposed) { return; }

        CancellationTokenSource? previous = _copiedCts;
        _copiedCts = null;

        if (previous is not null)
        {
            await previous.CancelAsync();
            previous.Dispose();
        }

        var cts = new CancellationTokenSource();
        _copiedCts = cts;
        _showCopied = true;
        StateHasChanged();

        try
        {
            await Task.Delay(s_copiedFeedbackDuration, cts.Token);

            if (ReferenceEquals(_copiedCts, cts))
            {
                _showCopied = false;
                StateHasChanged();
            }
        }
        catch (TaskCanceledException) { /* Feedback cycle cancelled by the next copy or by disposal. */ }
    }

    private async Task ShowOnlyUnresolvedAsync()
    {
        FilterLensCommands.ExcludeValue(EventProperty.ResolutionStatus, ResolutionStatusTokens.Resolved, _originLog);
        await CompleteAsync(true);
    }

    private IEnumerable<ProviderCoverageRow> SortRows(IEnumerable<ProviderCoverageRow> rows)
    {
        if (_sortColumn == CoverageSortColumn.Provider)
        {
            return _sortDescending ?
                rows.OrderByDescending(row => row.Provider, StringComparer.Ordinal) :
                rows.OrderBy(row => row.Provider, StringComparer.Ordinal);
        }

        Func<ProviderCoverageRow, int> key = _sortColumn switch
        {
            CoverageSortColumn.Total => row => row.Counts.Total,
            CoverageSortColumn.Resolved => row => row.Counts.Resolved,
            CoverageSortColumn.NoProvider => row => row.Counts.NoProvider,
            CoverageSortColumn.NoMessage => row => row.Counts.NoMessage,
            CoverageSortColumn.Failed => row => row.Counts.Failed,
            CoverageSortColumn.Coverage => row => (int)row.Status,
            _ => row => row.Counts.Unresolved
        };

        return _sortDescending ?
            rows.OrderByDescending(key).ThenBy(row => row.Provider, StringComparer.Ordinal) :
            rows.OrderBy(key).ThenBy(row => row.Provider, StringComparer.Ordinal);
    }

    private async Task ToggleExpandAsync(ProviderCoverageRow row)
    {
        if (IsExpanded(row))
        {
            CollapseDetail();

            return;
        }

        _expandedProvider = row.Provider;
        await LoadDetailAsync(row);
    }

    private void ToggleSort(CoverageSortColumn column)
    {
        if (_sortColumn == column)
        {
            _sortDescending = !_sortDescending;

            return;
        }

        _sortColumn = column;

        // Provider is textual and reads best ascending; numeric columns lead with the largest counts.
        _sortDescending = column != CoverageSortColumn.Provider;
    }
}
