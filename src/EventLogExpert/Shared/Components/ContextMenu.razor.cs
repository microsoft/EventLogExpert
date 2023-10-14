// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.Services;
using EventLogExpert.UI;
using EventLogExpert.UI.Store.EventLog;
using Fluxor;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components;

public partial class ContextMenu
{
    [Inject] private IClipboardService ClipboardService { get; set; } = null!;

    [Inject]
    private IStateSelection<EventLogState, DisplayEventModel?> SelectedEventState { get; set; } = null!;

    protected override void OnInitialized()
    {
        SelectedEventState.Select(s => s.SelectedEvent);

        base.OnInitialized();
    }

    private void CopySelected(CopyType? copyType) =>
        ClipboardService.CopySelectedEvent(SelectedEventState.Value, copyType);

    private void FilterEvent(FilterType filterType) { }
}
