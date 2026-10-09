// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Localization;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Stats;
using EventLogExpert.Runtime.StatusBar;
using EventLogExpert.UI.Common.Interop;
using EventLogExpert.UI.Focus;
using EventLogExpert.UI.Modal;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace EventLogExpert.UI.StatusBar;

public sealed partial class StatusBar
{
    private IJSObjectReference? _focusModule;
    private ElementReference _groupDirButton;
    private DisplayIndicatorState _indicatorState = null!;
    private ColumnName? _pendingChipColumn;
    private PendingChipFocus _pendingChipFocus = PendingChipFocus.None;
    private EventLogId? _pendingChipTabId;
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

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

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

        await JsModuleInterop.DisposeModuleSafelyAsync(_focusModule);
        _focusModule = null;

        await base.DisposeAsyncCore(disposing);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_pendingChipFocus != PendingChipFocus.None)
        {
            var ordering = Presentation.Ordering;

            // Mirror the markup's chip-visibility gate exactly (hasActiveTab && RequestedX is not null): the arm must be
            // driven by whether the CHIP is in the DOM, not by ordering state alone. When the active tab closes the chip
            // unmounts even though the (masked) ordering column can still be non-null - treating that as "gone" disarms
            // here instead of leaving a live arm that would later steal focus on an unrelated clear.
            bool hasActiveTab = Presentation.ActiveTabId is not null;
            bool sortChipPresent = hasActiveTab && ordering.RequestedOrderBy is not null;
            bool groupChipPresent = hasActiveTab && ordering.RequestedGroupBy is not null;

            ColumnName? clearedAxisColumn = _pendingChipFocus == PendingChipFocus.SortCleared ?
                ordering.RequestedOrderBy :
                ordering.RequestedGroupBy;

            bool clearedChipPresent = _pendingChipFocus == PendingChipFocus.SortCleared ? sortChipPresent : groupChipPresent;

            bool supersededByDifferentColumn = Presentation.State != PresentationState.Faulted &&
                clearedChipPresent &&
                clearedAxisColumn != _pendingChipColumn;

            bool clearedChipGone = !clearedChipPresent;

            // The arm belongs to the tab it was raised on; ordering state is global, so if the active tab changes
            // (closes, or auto-switches to a sibling) the chip the user cleared is no longer in their context.
            bool tabContextChanged = Presentation.ActiveTabId != _pendingChipTabId;

            if (supersededByDifferentColumn || tabContextChanged)
            {
                // Superseded by a different column, or the active tab changed while the arm was live: disarm without
                // moving focus - the clear's context is gone, and stealing focus to the status bar from wherever the
                // user has since moved is exactly the misfire this guards against.
                _pendingChipFocus = PendingChipFocus.None;
                _pendingChipColumn = null;
                _pendingChipTabId = null;
            }
            else if (clearedChipGone)
            {
                // Same tab, the cleared chip unmounted because the clear adopted; restore focus to the sibling
                // direction chip, or the stats button when this was the only chip. But a fault can defer the clear for
                // an arbitrary time while the chip stays masked-present; if the user tabbed or clicked to another control
                // in the meantime, the unmount did not orphan their focus, so restoring would steal it. The restore
                // guards on that inside a single JS round trip (focusIfNotElsewhere): it moves focus only when focus
                // currently rests on the document root - no real control holds it, whatever put it there - rather than
                // when the user has since landed on another control.
                ElementReference target = _pendingChipFocus == PendingChipFocus.SortCleared ?
                    (groupChipPresent ? _groupDirButton : _statsButton) :
                    (sortChipPresent ? _sortDirButton : _statsButton);

                _pendingChipFocus = PendingChipFocus.None;
                _pendingChipColumn = null;
                _pendingChipTabId = null;

                if (!string.IsNullOrEmpty(target.Id))
                {
                    await RestoreClearedChipFocusAsync(target, preventScroll: true);
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
        _pendingChipTabId = Presentation.ActiveTabId;
        _pendingChipFocus = PendingChipFocus.GroupCleared;

        LogTableCommands.SetGroupBy(null);
    }

    private void ClearSort(ColumnName column)
    {
        _pendingChipColumn = column;
        _pendingChipTabId = Presentation.ActiveTabId;
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

    private async ValueTask RestoreClearedChipFocusAsync(ElementReference target, bool preventScroll)
    {
        try
        {
            _focusModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>(
                "import",
                "./_content/EventLogExpert.UI/Common/focusGuard.js");

            await _focusModule.InvokeAsync<bool>("focusIfNotElsewhere", target, preventScroll);

            return;
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
        catch (ObjectDisposedException) { }
        catch (TaskCanceledException) { }

        await ElementFocus.TrySafelyAsync(target, preventScroll);
    }

    private void ToggleGroup() => LogTableCommands.ToggleGroupSortDirection();

    private void ToggleSort() => LogTableCommands.ToggleSortDirection();

    private void ToggleStats() => StatsCommands.SetVisible(!StatsVisibility.IsVisible);
}
