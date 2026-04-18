// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Services;
using EventLogExpert.UI;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Store.EventLog;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Collections.Immutable;

namespace EventLogExpert.Components;

public sealed partial class DetailsPane
{
    private DotNetObjectReference<DetailsPane>? _dotNetRef;
    private bool _hasOpened = false;
    private bool _isVisible = false;
    private bool _isXmlVisible = false;

    [Inject] private IClipboardService ClipboardService { get; init; } = null!;

    private string IsVisible => (SelectedEvent is not null && _isVisible).ToString().ToLower();

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private IPreferencesProvider PreferencesProvider { get; init; } = null!;

    private DisplayEventModel? SelectedEvent { get; set; }

    [Inject] private IStateSelection<EventLogState, ImmutableList<DisplayEventModel>> SelectedEventSelection { get; init; } = null!;

    [Inject] private ISettingsService Settings { get; init; } = null!;

    [Inject] private ITraceLogger TraceLogger { get; init; } = null!;

    [JSInvokable]
    public void OnDetailsPaneHeightChanged(int height)
    {
        if (height > 0)
        {
            PreferencesProvider.DetailsPaneHeightPreference = height;
        }
    }

    protected override async ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing)
        {
            SelectedEventSelection.SelectedValueChanged -= OnSelectedEventChanged;

            try
            {
                await JSRuntime.InvokeVoidAsync("disposeDetailsPaneResizer");
            }
            catch (JSDisconnectedException) { }

            _dotNetRef?.Dispose();
        }

        await base.DisposeAsyncCore(disposing);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _dotNetRef = DotNetObjectReference.Create(this);
            await JSRuntime.InvokeVoidAsync(
                "enableDetailsPaneResizer",
                _dotNetRef,
                PreferencesProvider.DetailsPaneHeightPreference);
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override void OnInitialized()
    {
        SelectedEventSelection.Select(s => s.SelectedEvents);

        SelectedEventSelection.SelectedValueChanged += OnSelectedEventChanged;

        base.OnInitialized();
    }

    private void CopyEvent() => ClipboardService.CopySelectedEvent(CopyType.Full);

    private void HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Repeat) { return; }

        if (e.Key is "Enter" or " ")
        {
            ToggleMenu();
        }
    }

    private void HandleKeyDownXml(KeyboardEventArgs e)
    {
        if (e.Repeat) { return; }

        if (e.Key is "Enter" or " ")
        {
            ToggleXml();
        }
    }

    private async void OnSelectedEventChanged(object? sender, ImmutableList<DisplayEventModel> selectedEvents)
    {
        try
        {
            SelectedEvent = selectedEvents.LastOrDefault();

            if (SelectedEvent is null) { return; }

            await SelectedEvent.ResolveXml();

            if (!_hasOpened || Settings.ShowDisplayPaneOnSelectionChange) { _isVisible = true; }

            await InvokeAsync(StateHasChanged);
        }
        catch (Exception e)
        {
            TraceLogger.Error($"Failed to handle selected event change: {e}");
        }
    }

    private void ToggleMenu()
    {
        if (!_hasOpened) { _hasOpened = true; }

        _isVisible = !_isVisible;
    }

    private void ToggleXml() => _isXmlVisible = !_isXmlVisible;
}
