// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.Concurrency;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Stats;
using EventLogExpert.UI.Modal;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.LogTable.Stats;

public sealed partial class StatsDetailModal : ModalBase<bool>
{
    // Bounds the full-list scan so a dimension with near-event-count cardinality (Source / User) can't build an
    // unbounded list; the search box narrows within what was collected.
    private const int MaxRows = 2000;

    private readonly CancellationTokenSource _cts = new();

    private IReadOnlyList<StatsContributor> _all = [];
    private int _distinct;
    private bool _failed;
    private bool _loading = true;
    private string _search = string.Empty;
    private int _total;

    [EditorRequired]
    [Parameter] public StatsDimension Dimension { get; set; }

    [Parameter] public string? OriginLog { get; set; }

    [EditorRequired]
    [Parameter] public IEventColumnView View { get; set; } = null!;

    [Inject] private ICpuWorkScheduler CpuScheduler { get; init; } = null!;

    [Inject] private IFilterLensCommands FilterLensCommands { get; init; } = null!;

    [Inject] private IStringLocalizer<SharedResource> Localizer { get; init; } = null!;

    [Inject] private IStatsService StatsService { get; init; } = null!;

    private IEnumerable<StatsContributor> VisibleRows
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_search)) { return _all; }

            string needle = _search.Trim();
            return _all.Where(row => row.Value.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }
    }

    protected override ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing)
        {
            _cts.Cancel();
            _cts.Dispose();
        }

        return base.DisposeAsyncCore(disposing);
    }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();

        try
        {
            // UserInitiated: user opened this modal and is watching its spinner. Do NOT add ConfigureAwait(false) - the
            // continuation mutates state and the finally calls StateHasChanged() without InvokeAsync, so it must resume on the Blazor dispatcher.
            DimensionStats stats = await CpuScheduler.RunAsync(
                detailToken => StatsService.BuildDimension(View, Dimension, MaxRows, detailToken), CpuWorkPriority.UserInitiated, _cts.Token);

            _all = stats.Top;
            _distinct = stats.DistinctCount;
            _total = stats.Total;
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

    private static string FormatCount(int value) => TallyFormatter.Count(value);

    private void Exclude(StatsContributor row) =>
        Dimension.PushRowFilter(FilterLensCommands, row.Value, OriginLog, include: false);

    private string FormatShare(int count) => TallyFormatter.Share(count, _total);

    private void Include(StatsContributor row) =>
        Dimension.PushRowFilter(FilterLensCommands, row.Value, OriginLog, include: true);
}
