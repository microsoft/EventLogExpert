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

    private ChipFocusTarget _pendingChipFocus = ChipFocusTarget.None;

    private ElementReference _sortDirButton;

    private ElementReference _statsButton;

    private enum ChipFocusTarget
    {
        None,
        SortDirection,
        GroupDirection,
        Stats
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

    // Clearing a chip unmounts the button that had focus; without this, focus falls to <body>. Move it to the sibling
    // ordering chip's direction toggle when one remains, otherwise to the always-present stats button (WAI-ARIA APG
    // guidance for removing a focused item from a set). The pending target is set only by the chip [x] handlers.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_pendingChipFocus != ChipFocusTarget.None)
        {
            ElementReference target = _pendingChipFocus switch
            {
                ChipFocusTarget.GroupDirection => _groupDirButton,
                ChipFocusTarget.SortDirection => _sortDirButton,
                _ => _statsButton
            };

            _pendingChipFocus = ChipFocusTarget.None;

            if (!string.IsNullOrEmpty(target.Id))
            {
                await ElementFocus.TrySafelyAsync(target, preventScroll: true);
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

    private void ClearGroup()
    {
        _pendingChipFocus = Presentation.Ordering.RequestedOrderBy is not null ?
            ChipFocusTarget.SortDirection :
            ChipFocusTarget.Stats;

        LogTableCommands.SetGroupBy(null);
    }

    private void ClearSort()
    {
        _pendingChipFocus = Presentation.Ordering.RequestedGroupBy is not null ?
            ChipFocusTarget.GroupDirection :
            ChipFocusTarget.Stats;

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
