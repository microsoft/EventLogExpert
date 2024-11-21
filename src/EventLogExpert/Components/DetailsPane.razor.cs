// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.Services;
using EventLogExpert.UI;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.Settings;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Collections.Immutable;

namespace EventLogExpert.Components;

public sealed partial class DetailsPane
{
    private bool _hasOpened = false;
    private bool _isVisible = false;
    private bool _isXmlVisible = false;

    [Inject] private IClipboardService ClipboardService { get; init; } = null!;

    private string IsVisible => (SelectedEvent is not null && _isVisible).ToString().ToLower();

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    private DisplayEventModel? SelectedEvent { get; set; }

    [Inject] private IStateSelection<EventLogState, ImmutableList<DisplayEventModel>> SelectedEventSelection { get; init; } = null!;

    [Inject] private IState<SettingsState> SettingsState { get; init; } = null!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await JSRuntime.InvokeVoidAsync("enableDetailsPaneResizer");
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override void OnInitialized()
    {
        SelectedEventSelection.Select(s => s.SelectedEvents);

        SelectedEventSelection.SelectedValueChanged += async (s, selectedEvents) =>
        {
            SelectedEvent = selectedEvents.LastOrDefault();

            if (SelectedEvent is null ||
                (_hasOpened && !SettingsState.Value.Config.ShowDisplayPaneOnSelectionChange))
            {
                return;
            }

            await SelectedEvent.ResolveXml();

            _isVisible = true;

            StateHasChanged();
        };

        base.OnInitialized();
    }

    private void CopyEvent() => ClipboardService.CopySelectedEvent(CopyType.Full);

    private void ToggleMenu()
    {
        if (!_hasOpened) { _hasOpened = true; }

        _isVisible = !_isVisible;
    }

    private void ToggleXml() => _isXmlVisible = !_isXmlVisible;
}
