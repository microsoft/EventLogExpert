// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.StatusBar;
using Microsoft.AspNetCore.Components;
using System.Collections.Immutable;

namespace EventLogExpert.UI.StatusBar;

public sealed partial class StatusBar
{
    private DisplayIndicatorState _indicatorState = null!;

    [Inject] private IFilterAppliedSource FilterApplied { get; init; } = null!;

    [Inject] private DisplayIndicatorGate IndicatorGate { get; init; } = null!;

    [Inject] private IFilterLensSource LensSource { get; init; } = null!;

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

        base.OnInitialized();
    }

    private static int TotalRawCount(LogView activeTable, ImmutableList<LogTabGroup> groups, StatusBarPresentation status)
    {
        if (activeTable.GroupId?.IsAll == true) { return status.RawEventTotal; }

        if (activeTable.GroupId is not { } groupId)
        {
            return status.RawEventCountsByLog.GetValueOrDefault(activeTable.Id, 0);
        }

        var group = groups.FirstOrDefault(candidate => candidate.Id == groupId);

        return group is null ? 0 : group.MemberIds.Sum(id => status.RawEventCountsByLog.GetValueOrDefault(id, 0));
    }

    private void RequestIndicatorRender() => RequestGuardedRender(StateHasChanged);

    private DisplayIndicatorKind ResolveIndicator()
    {
        var shown = _indicatorState.Resolve(Presentation.IndicatorKind, Presentation.Revision);

        _indicatorState.RecordPaint(shown);

        return shown.Sentence;
    }
}
