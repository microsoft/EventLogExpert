// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Stats;
using EventLogExpert.Runtime.StatusBar;
using EventLogExpert.UI.Focus;
using EventLogExpert.UI.Modal;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.StatusBar;

public sealed partial class StatusBar
{
    private ElementReference _groupDirButton;
    private DisplayIndicatorState _indicatorState = null!;
    private ColumnName? _pendingChipColumn;
    private PendingChipFocus _pendingChipFocus = PendingChipFocus.None;
    private ElementReference _sortDirButton;
    private ElementReference _statsButton;

    private enum PendingChipFocus
    {
        None,
        SortCleared,
        GroupCleared
    }

    [Inject] private IEventLogCommands EventLogCommands { get; init; } = null!;

    [Inject] private IFilterAppliedSource FilterApplied { get; init; } = null!;

    [Inject] private DisplayIndicatorGate IndicatorGate { get; init; } = null!;

    [Inject] private IFilterLensSource LensSource { get; init; } = null!;

    [Inject] private IStringLocalizer<SharedResource> Localizer { get; init; } = null!;

    [Inject] private ILogTableCommands LogTableCommands { get; init; } = null!;

    [Inject] private IModalCoordinator ModalCoordinator { get; init; } = null!;

    [Inject] private IStatsCommands StatsCommands { get; init; } = null!;

    [Inject] private IStatsVisibilitySource StatsVisibility { get; init; } = null!;

    [Inject] private IStatusBarSource StatusBarSource { get; init; } = null!;

    protected override async ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing)
        {
            _indicatorState?.Dispose();
        }

        await base.DisposeAsyncCore(disposing);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_pendingChipFocus != PendingChipFocus.None)
        {
            var ordering = Presentation.Ordering;

            ColumnName? clearedAxisColumn = _pendingChipFocus == PendingChipFocus.SortCleared ?
                ordering.RequestedOrderBy :
                ordering.RequestedGroupBy;

            bool supersededByDifferentColumn = Presentation.State != PresentationState.Faulted &&
                clearedAxisColumn is { } boundColumn &&
                boundColumn != _pendingChipColumn;

            bool clearedChipGone = clearedAxisColumn is null;

            if (supersededByDifferentColumn)
            {
                _pendingChipFocus = PendingChipFocus.None;
                _pendingChipColumn = null;
            }
            else if (clearedChipGone)
            {
                ElementReference target = _pendingChipFocus == PendingChipFocus.SortCleared ?
                    (ordering.RequestedGroupBy is not null ? _groupDirButton : _statsButton) :
                    (ordering.RequestedOrderBy is not null ? _sortDirButton : _statsButton);

                _pendingChipFocus = PendingChipFocus.None;
                _pendingChipColumn = null;

                if (!string.IsNullOrEmpty(target.Id))
                {
                    await ElementFocus.TrySafelyAsync(target, preventScroll: true);
                }
            }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override void OnInitialized()
    {
        _indicatorState = new DisplayIndicatorState(IndicatorGate, RequestIndicatorRender);

        ObserveSource(StatusBarSource);
        ObserveSource(FilterApplied);
        ObserveSource(LensSource);
        ObserveSource(StatsVisibility);

        base.OnInitialized();
    }

    private void ClearGroup(ColumnName column)
    {
        _pendingChipColumn = column;
        _pendingChipFocus = PendingChipFocus.GroupCleared;

        LogTableCommands.SetGroupBy(null);
    }

    private void ClearSort(ColumnName column)
    {
        _pendingChipColumn = column;
        _pendingChipFocus = PendingChipFocus.SortCleared;

        LogTableCommands.SetOrderBy(null);
    }

    private void LoadNewEvents() => EventLogCommands.LoadNewEvents();

    private void OpenCoverage() => _ = ModalCoordinator.OpenResolutionCoverageAsync();

    private void RequestIndicatorRender() => RequestGuardedRender(StateHasChanged);

    private DisplayIndicatorKind ResolveIndicator()
    {
        var shown = _indicatorState.Resolve(Presentation.IndicatorKind, Presentation.Revision);

        _indicatorState.RecordPaint(shown);

        return shown.Sentence;
    }

    private void ToggleGroup() => LogTableCommands.ToggleGroupSortDirection();

    private void ToggleSort() => LogTableCommands.ToggleSortDirection();

    private void ToggleStats() => StatsCommands.SetVisible(!StatsVisibility.IsVisible);
}
