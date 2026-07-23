// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.DetailsPane;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.Common.Interop;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Xml;
using System.Xml.Linq;

namespace EventLogExpert.UI.DetailsPane;

public sealed partial class DetailsPane
{
    private readonly HashSet<int> _expandedFields = [];

    private DetailsTab _activeTab = DetailsTab.Reader;
    private IJSObjectReference? _detailsPaneModule;
    private DotNetObjectReference<DetailsPane>? _dotNetRef;
    private bool _hasOpened;
    private bool _isExpanded;
    private DetailsReaderModel? _model;
    private string? _resolvedXml;
    private ResolvedEvent? _selectedEvent;
    /// <summary>
    ///     Locator of the current focus, used to detect a stale async XML resolution: a re-resolve mid-fetch mints a new
    ///     <see cref="ResolvedEvent" />, so reference identity can't gate the result but the locator can.
    /// </summary>
    private EventLocator? _selectedHandle;
    /// <summary>Cancels any in-flight XML resolution when the selection changes again before completion.</summary>
    private CancellationTokenSource? _xmlResolveCts;

    private enum DetailsTab
    {
        Reader,
        Xml
    }

    [Inject] private IStateSelection<LogTableState, EventLogId?> ActiveLog { get; init; } = null!;

    [Inject] private IClipboardService Clipboard { get; init; } = null!;

    [Inject] private IEventXmlResolver EventXmlResolver { get; init; } = null!;

    [Inject] private IFilterLensCommands FilterLensCommands { get; init; } = null!;

    [Inject] private IStateSelection<EventLogState, SelectionEntry?> Focus { get; init; } = null!;

    private string IsExpanded => _isExpanded.ToString().ToLowerInvariant();

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private IState<LogTableState> LogTableState { get; init; } = null!;

    [Inject] private IDetailsPanePreferencesProvider PreferencesProvider { get; init; } = null!;

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
            Focus.SelectedValueChanged -= OnFocusChanged;
            ActiveLog.SelectedValueChanged -= OnActiveLogChanged;
            Settings.TimeZoneChanged -= OnTimeZoneChanged;

            try { _xmlResolveCts?.Cancel(); } catch (ObjectDisposedException) { /* CTS already disposed; cancel is moot. */ }

            _xmlResolveCts?.Dispose();

            await JsModuleInterop.DisposeModuleSafelyAsync(
                _detailsPaneModule,
                static module => module.InvokeVoidAsync("disposeDetailsPaneResizer"));

            _detailsPaneModule = null;

            _dotNetRef?.Dispose();
        }

        await base.DisposeAsyncCore(disposing);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _dotNetRef = DotNetObjectReference.Create(this);

            _detailsPaneModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
                "import",
                "./_content/EventLogExpert.UI/DetailsPane/DetailsPane.razor.js");

            await _detailsPaneModule.InvokeVoidAsync(
                "enableDetailsPaneResizer",
                _dotNetRef,
                PreferencesProvider.DetailsPaneHeightPreference);
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override void OnInitialized()
    {
        Focus.Select(s => s.Focus);
        Focus.SelectedValueChanged += OnFocusChanged;

        // The active tab / log is the reset signal for the view tab (NOT the selected event's member log, which varies
        // per selection inside a combined view).
        ActiveLog.Select(s => s.ActiveEventLogId);
        ActiveLog.SelectedValueChanged += OnActiveLogChanged;

        // The reader model pre-renders timestamps in the configured zone, so a zone change must rebuild it.
        Settings.TimeZoneChanged += OnTimeZoneChanged;

        // Seed from the current store value so the pane reflects an existing focus (e.g., a restore that completed
        // before this component subscribed) instead of staying empty until the next change event.
        if (Focus.Value is not null)
        {
            OnFocusChanged(this, Focus.Value);
        }

        base.OnInitialized();
    }

    private async Task CopyEventAsync()
    {
        if (_model is { } model)
        {
            await Clipboard.CopyTextAsync(DetailsReaderFormatter.BuildEventCopyText(model));
        }
    }

    private async Task CopyFieldsAsync(IReadOnlyList<DetailsField> fields) =>
        await Clipboard.CopyTextAsync(DetailsReaderFormatter.BuildFieldsCopyText(fields));

    private async Task CopyPropertiesAsync(IReadOnlyList<DetailsProperty> properties) =>
        await Clipboard.CopyTextAsync(DetailsReaderFormatter.BuildPropertiesCopyText(properties));

    private async Task CopyValueAsync(DetailsField field) => await Clipboard.CopyTextAsync(field.CopyValue);

    private string GetXmlForDisplay()
    {
        if (string.IsNullOrEmpty(_resolvedXml)) { return string.Empty; }

        try
        {
            return XElement.Parse(_resolvedXml).ToString();
        }
        catch (XmlException ex)
        {
            TraceLogger.Trace($"{nameof(DetailsPane)}: failed to parse XML for display, falling back to raw text: {ex.Message}");

            return _resolvedXml;
        }
    }

    private bool IsFieldExpanded(int index) => _expandedFields.Contains(index);

    private async void OnActiveLogChanged(object? sender, EventLogId? activeLog)
    {
        try
        {
            _activeTab = DetailsTab.Reader;

            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            TraceLogger.Error($"{nameof(DetailsPane)}: failed to handle active-log change: {ex}");
        }
    }

    private async void OnFocusChanged(object? sender, SelectionEntry? focus)
    {
        try
        {
            var handle = focus?.CurrentHandle;
            _selectedHandle = handle;

            // A null CurrentHandle (a selection whose row could not be re-resolved after a reload) resolves to nothing,
            // leaving _selectedEvent/_model null so the pane hides.
            ResolvedEvent? selectedEvent = null;

            if (handle is { } locator
                && LogTableState.Value.EventsForLog(locator.LogId).TryGetDetail(locator, out var detail))
            {
                selectedEvent = detail;
            }

            _selectedEvent = selectedEvent;
            _model = selectedEvent is { } resolved ? DetailsReaderFormatter.BuildModel(resolved, Settings.TimeZoneInfo) : null;
            _expandedFields.Clear();

            // Cancel any in-flight resolution from a prior selection so a stale fetch
            // can't overwrite the resolved XML for the now-current selection.
            try { _xmlResolveCts?.Cancel(); } catch (ObjectDisposedException) { /* CTS already disposed; cancel is moot. */ }

            _xmlResolveCts?.Dispose();
            _xmlResolveCts = null;

            _resolvedXml = null;

            if (selectedEvent is null)
            {
                await InvokeAsync(StateHasChanged);

                return;
            }

            // The pane stays closed until the user opens something: auto-expand on the FIRST selection, but after the
            // user has toggled it (_hasOpened) respect their choice on later selections unless the preference opts into
            // always-expand-on-select.
            if (!_hasOpened || PreferencesProvider.DisplayPaneSelectionPreference) { _isExpanded = true; }

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
                    TraceLogger.Error($"{nameof(DetailsPane)}: XML resolution failed for selected event: {ex}");

                    // Only surface the failure if we're still the current selection; otherwise a newer selection owns
                    // _resolvedXml. A re-resolve mints a new event instance, so compare the stable locator, not object identity.
                    if (_selectedHandle == handle && ReferenceEquals(_xmlResolveCts, cts))
                    {
                        _resolvedXml = string.Empty;
                    }

                    return;
                }

                // Selection changed while the fetch was in flight; discard the stale result.
                if (cts.IsCancellationRequested || _selectedHandle != handle)
                {
                    return;
                }

                _resolvedXml = xml;
            }
            finally
            {
                // Always release the per-selection CTS (success, cancel, or failure), but guard against clobbering a
                // newer CTS installed by a subsequent OnFocusChanged during our await.
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
            TraceLogger.Error($"{nameof(DetailsPane)}: failed to handle selected event change: {e}");
        }
    }

    private async void OnTimeZoneChanged(object? sender, TimeZoneInfo timeZone)
    {
        try
        {
            if (_selectedEvent is { } detail)
            {
                _model = DetailsReaderFormatter.BuildModel(detail, timeZone);
            }

            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            TraceLogger.Error($"{nameof(DetailsPane)}: failed to handle time-zone change: {ex}");
        }
    }

    private void SetTab(DetailsTab tab) => _activeTab = tab;

    private void ToggleFieldExpansion(int index)
    {
        if (!_expandedFields.Add(index)) { _expandedFields.Remove(index); }
    }

    private void ToggleMenu()
    {
        _hasOpened = true;
        _isExpanded = !_isExpanded;
    }
}
