// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.EventLog;
using Fluxor;
using Fluxor.Blazor.Web.Components;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Layout;

public sealed partial class MainContent : FluxorComponent
{
    [Inject]
    private IStateSelection<EventLogState, bool> HasActiveLogs { get; init; } = null!;

    protected override void OnInitialized()
    {
        HasActiveLogs.Select(state => !state.ActiveLogs.IsEmpty);
        base.OnInitialized();
    }
}
