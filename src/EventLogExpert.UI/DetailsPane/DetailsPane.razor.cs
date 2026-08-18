// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

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
    private bool _disposed;
    private DotNetObjectReference<DetailsPane>? _dotNetRef;
    private bool _hasOpened;
    private bool _isExpanded;
    private DetailsReaderModel? _model;
    private string? _resolvedXml;
    private ResolvedEvent? _selectedEvent;
    private EventLocator? _selectedHandle;
    private CancellationTokenSource? _xmlResolveCts;

    private enum DetailsTab
    {
        Reader,
        Xml,
        Correlation
    }

    [Inject] private IActiveEventLogSource ActiveEventLog { get; init; } = null!;

    [Inject] private IClipboardService Clipboard { get; init; } = null!;

    [Inject] private IEventDetailResolver DetailResolver { get; init; } = null!;

    [Inject] private IEventFocusSource EventFocus { get; init; } = null!;

    [Inject] private IEventXmlResolver EventXmlResolver { get; init; } = null!;

    [Inject] private IFilterLensCommands FilterLensCommands { get; init; } = null!;

    private string IsExpanded => _isExpanded.ToString().ToLowerInvariant();

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

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
            _disposed = true;

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
        ObserveSource(EventFocus, OnFocusChangedAsync);
        ObserveSource(ActiveEventLog, OnActiveLogChangedAsync);

        Settings.TimeZoneChanged += OnTimeZoneChanged;

        if (EventFocus.Current is not null)
        {
            _ = OnFocusChangedAsync();
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

    private Task OnActiveLogChangedAsync()
    {
        if (!_disposed)
        {
            _activeTab = DetailsTab.Reader;
            StateHasChanged();
        }

        return Task.CompletedTask;
    }

    private async Task OnFocusChangedAsync()
    {
        try
        {
            if (_disposed) { return; }

            var focus = EventFocus.Current;
            var handle = focus?.CurrentHandle;
            _selectedHandle = handle;

            ResolvedEvent? selectedEvent = null;

            if (handle is { } locator && DetailResolver.TryResolve(locator, out var detail))
            {
                selectedEvent = detail;
            }

            _selectedEvent = selectedEvent;
            _model = selectedEvent is { } resolved ? DetailsReaderFormatter.BuildModel(resolved, Settings.TimeZoneInfo) : null;
            _expandedFields.Clear();

            try { _xmlResolveCts?.Cancel(); } catch (ObjectDisposedException) { /* CTS already disposed; cancel is moot. */ }

            _xmlResolveCts?.Dispose();
            _xmlResolveCts = null;

            _resolvedXml = null;

            if (selectedEvent is null)
            {
                StateHasChanged();

                return;
            }

            if (!_hasOpened || PreferencesProvider.DisplayPaneSelectionPreference) { _isExpanded = true; }

            if (!string.IsNullOrEmpty(selectedEvent.Xml))
            {
                _resolvedXml = selectedEvent.Xml;

                StateHasChanged();

                return;
            }

            StateHasChanged();

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

                    // Only surface the failure if we're still the current selection (locally AND per the source);
                    // otherwise a newer selection owns _resolvedXml. A re-resolve mints a new event instance, so compare
                    // the stable locator, not object identity.
                    if (!_disposed && _selectedHandle == handle && ReferenceEquals(_xmlResolveCts, cts) && EventFocus.Current == focus)
                    {
                        _resolvedXml = string.Empty;
                    }

                    return;
                }

                if (_disposed || cts.IsCancellationRequested || _selectedHandle != handle || EventFocus.Current != focus)
                {
                    return;
                }

                _resolvedXml = xml;
            }
            finally
            {
                if (ReferenceEquals(_xmlResolveCts, cts))
                {
                    _xmlResolveCts = null;
                }

                cts.Dispose();

                if (!_disposed) { StateHasChanged(); }
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
