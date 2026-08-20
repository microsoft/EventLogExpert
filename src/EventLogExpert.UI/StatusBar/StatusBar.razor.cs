// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Stats;
using EventLogExpert.Runtime.StatusBar;
using EventLogExpert.UI.Modal;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.StatusBar;

public sealed partial class StatusBar
{
    private DisplayIndicatorState _indicatorState = null!;

    [Inject] private IFilterAppliedSource FilterApplied { get; init; } = null!;

    [Inject] private DisplayIndicatorGate IndicatorGate { get; init; } = null!;

    [Inject] private IFilterLensSource LensSource { get; init; } = null!;

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

    protected override void OnInitialized()
    {
        _indicatorState = new DisplayIndicatorState(IndicatorGate, RequestIndicatorRender);

        ObserveSource(StatusBarSource);
        ObserveSource(FilterApplied);
        ObserveSource(LensSource);
        ObserveSource(StatsVisibility);

        base.OnInitialized();
    }

    private void OpenCoverage() => _ = ModalCoordinator.OpenResolutionCoverageAsync();

    private void RequestIndicatorRender() => RequestGuardedRender(StateHasChanged);

    private DisplayIndicatorKind ResolveIndicator()
    {
        var shown = _indicatorState.Resolve(Presentation.IndicatorKind, Presentation.Revision);

        _indicatorState.RecordPaint(shown);

        return shown.Sentence;
    }

    private void ToggleStats() => StatsCommands.SetVisible(!StatsVisibility.IsVisible);
}
