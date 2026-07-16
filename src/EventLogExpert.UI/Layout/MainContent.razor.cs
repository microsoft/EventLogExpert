// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.Histogram;
using Fluxor;
using Fluxor.Blazor.Web.Components;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Layout;

public sealed partial class MainContent : FluxorComponent
{
    [Inject]
    private IStateSelection<EventLogState, bool> HasActiveLogs { get; init; } = null!;

    [Inject]
    private IStateSelection<HistogramState, bool> HistogramVisible { get; init; } = null!;

    protected override void OnInitialized()
    {
        HasActiveLogs.Select(state => state.OpenLogCount > 0);
        HistogramVisible.Select(state => state.IsVisible);
        base.OnInitialized();
    }
}
