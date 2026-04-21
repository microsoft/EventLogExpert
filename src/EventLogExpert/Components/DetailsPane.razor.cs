// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventResolvers;
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
using System.Xml;
using System.Xml.Linq;

namespace EventLogExpert.Components;

public sealed partial class DetailsPane
{
    private DotNetObjectReference<DetailsPane>? _dotNetRef;
    private bool _hasOpened;
    private bool _isVisible;
    private bool _isXmlVisible;

    /// <summary>Resolved XML for the currently selected event. <c>null</c> means a fetch is in flight
    /// (show "Resolving XML..."); empty string means resolved-with-no-content (e.g. live-watcher event
    /// without a record id, or render failure); any other value is the rendered XML.</summary>
    private string? _resolvedXml;

    /// <summary>Cancels any in-flight XML resolution when the selection changes again before completion.</summary>
    private CancellationTokenSource? _xmlResolveCts;

    [Inject] private IClipboardService ClipboardService { get; init; } = null!;

    [Inject] private IEventXmlResolver EventXmlResolver { get; init; } = null!;

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

            try { _xmlResolveCts?.Cancel(); } catch (ObjectDisposedException) { }
            _xmlResolveCts?.Dispose();

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

    private async Task CopyEvent() => await ClipboardService.CopySelectedEvent(CopyType.Full);

    private string GetXmlForDisplay()
    {
        if (string.IsNullOrEmpty(_resolvedXml)) { return string.Empty; }

        try
        {
            return XElement.Parse(_resolvedXml).ToString();
        }
        catch (XmlException ex)
        {
            TraceLogger.Trace($"DetailsPane: failed to parse XML for display, falling back to raw text: {ex.Message}");

            return _resolvedXml;
        }
    }

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
            var selectedEvent = selectedEvents.LastOrDefault();
            SelectedEvent = selectedEvent;

            // Cancel any in-flight resolution from a prior selection so a stale fetch
            // can't overwrite the resolved XML for the now-current selection.
            try { _xmlResolveCts?.Cancel(); } catch (ObjectDisposedException) { }
            _xmlResolveCts?.Dispose();
            _xmlResolveCts = null;

            _resolvedXml = null;

            if (selectedEvent is null)
            {
                await InvokeAsync(StateHasChanged);

                return;
            }

            if (!_hasOpened || Settings.ShowDisplayPaneOnSelectionChange) { _isVisible = true; }

            // Short-circuit: live-watcher events arrive with XML already pre-rendered. Skip the
            // resolver round-trip (and the "Resolving XML..." flicker) when we already have it.
            if (!string.IsNullOrEmpty(selectedEvent.Xml))
            {
                _resolvedXml = selectedEvent.Xml;

                await InvokeAsync(StateHasChanged);

                return;
            }

            // Render once with the loading sentinel before kicking off the async fetch.
            await InvokeAsync(StateHasChanged);

            var cts = new CancellationTokenSource();
            _xmlResolveCts = cts;

            try
            {
                string xml;

                try
                {
                    xml = await EventXmlResolver.GetXmlAsync(selectedEvent, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // A newer selection canceled this fetch; leave _resolvedXml alone so the new
                    // selection's pipeline owns it.
                    return;
                }
                catch (Exception ex)
                {
                    TraceLogger.Error($"DetailsPane: XML resolution failed for selected event: {ex}");

                    // Only surface the failure if we're still the current selection — otherwise
                    // a newer selection has taken over and owns _resolvedXml.
                    if (ReferenceEquals(SelectedEvent, selectedEvent) && ReferenceEquals(_xmlResolveCts, cts))
                    {
                        _resolvedXml = string.Empty;
                    }

                    return;
                }

                // Selection changed while the fetch was in flight — discard the stale result.
                if (cts.IsCancellationRequested || !ReferenceEquals(SelectedEvent, selectedEvent))
                {
                    return;
                }

                _resolvedXml = xml;
            }
            finally
            {
                // Always release the per-selection CTS regardless of success / cancel / failure.
                // Guard against clobbering a newer CTS that a subsequent OnSelectedEventChanged
                // call may have already installed during our await.
                if (ReferenceEquals(_xmlResolveCts, cts))
                {
                    _xmlResolveCts = null;
                }

                cts.Dispose();

                await InvokeAsync(StateHasChanged);
            }
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
