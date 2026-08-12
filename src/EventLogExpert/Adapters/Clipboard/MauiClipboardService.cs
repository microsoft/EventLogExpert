// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Settings;
using Fluxor;
using System.Collections.Immutable;
using MauiClipboard = Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard;

namespace EventLogExpert.Adapters.Clipboard;

public sealed class MauiClipboardService : IClipboardService
{
    private readonly IStateSelection<LogTableState, ImmutableList<ColumnName>> _columnOrder;
    private readonly IStateSelection<LogTableState, ImmutableDictionary<ColumnName, bool>> _eventTableColumns;
    private readonly IStateSelection<EventLogState, SelectionEntry?> _focus;
    private readonly IEventCopyFormatter _formatter;
    private readonly IStateSelection<EventLogState, ImmutableList<SelectionEntry>> _selection;
    private readonly ISettingsService _settings;
    private readonly ITraceLogger _traceLogger;

    public MauiClipboardService(
        IStateSelection<LogTableState, ImmutableDictionary<ColumnName, bool>> eventTableColumns,
        IStateSelection<LogTableState, ImmutableList<ColumnName>> columnOrder,
        IStateSelection<EventLogState, ImmutableList<SelectionEntry>> selection,
        IStateSelection<EventLogState, SelectionEntry?> focus,
        IEventCopyFormatter formatter,
        ISettingsService settings,
        ITraceLogger traceLogger)
    {
        _eventTableColumns = eventTableColumns;
        _columnOrder = columnOrder;
        _selection = selection;
        _focus = focus;
        _formatter = formatter;
        _settings = settings;
        _traceLogger = traceLogger;

        _eventTableColumns.Select(s => s.Columns);
        _columnOrder.Select(s => s.ColumnOrder);
        _selection.Select(s => s.Selection);
        _focus.Select(s => s.Focus);
    }

    public async Task CopySelectedEvent(EventCopyFormat? format = null)
    {
        // unhandled UI exception. Log and swallow to preserve fire-and-forget behavior.
        try
        {
            var request = new EventCopyRequest(
                _selection.Value,
                _focus.Value,
                _eventTableColumns.Value,
                _columnOrder.Value,
                format ?? _settings.CopyFormat,
                _settings.TimeZoneInfo);

            string stringToCopy = await _formatter.FormatAsync(request).ConfigureAwait(false);

            await MauiClipboard.SetTextAsync(stringToCopy).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _traceLogger.Error($"{nameof(MauiClipboardService)}.{nameof(CopySelectedEvent)}: failed: {ex}");
        }
    }

    public async Task CopyTextAsync(string text)
    {
        try
        {
            await MauiClipboard.SetTextAsync(text).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _traceLogger.Error($"{nameof(MauiClipboardService)}.{nameof(CopyTextAsync)}: failed: {ex}");
        }
    }
}
