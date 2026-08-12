// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.UI.Common;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Layout;

public sealed partial class MainContent : AppStateComponentBase
{
    [Inject]
    private IHistogramVisibilitySource HistogramVisibilitySource { get; init; } = null!;

    [Inject]
    private IOpenLogsPresenceSource OpenLogsSource { get; init; } = null!;

    protected override void OnInitialized()
    {
        ObserveSource(OpenLogsSource);
        ObserveSource(HistogramVisibilitySource);
        base.OnInitialized();
    }
}
