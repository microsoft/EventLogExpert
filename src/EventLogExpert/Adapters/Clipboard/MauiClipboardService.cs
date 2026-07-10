// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Common.Display;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Settings;
using Fluxor;
using System.Collections.Immutable;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using MauiClipboard = Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard;

namespace EventLogExpert.Adapters.Clipboard;

public sealed class MauiClipboardService : IClipboardService
{
    private readonly IStateSelection<LogTableState, ImmutableList<ColumnName>> _columnOrder;
    private readonly IStateSelection<LogTableState, ImmutableDictionary<ColumnName, bool>> _eventTableColumns;
    private readonly IStateSelection<EventLogState, SelectionEntry?> _focus;
    private readonly IState<LogTableState> _logTableState;
    private readonly IStateSelection<EventLogState, ImmutableList<SelectionEntry>> _selection;
    private readonly ISettingsService _settings;
    private readonly ITraceLogger _traceLogger;
    private readonly IEventXmlResolver _xmlResolver;

    public MauiClipboardService(
        IStateSelection<LogTableState, ImmutableDictionary<ColumnName, bool>> eventTableColumns,
        IStateSelection<LogTableState, ImmutableList<ColumnName>> columnOrder,
        IStateSelection<EventLogState, ImmutableList<SelectionEntry>> selection,
        IStateSelection<EventLogState, SelectionEntry?> focus,
        IState<LogTableState> logTableState,
        ISettingsService settings,
        IEventXmlResolver xmlResolver,
        ITraceLogger traceLogger)
    {
        _eventTableColumns = eventTableColumns;
        _columnOrder = columnOrder;
        _selection = selection;
        _focus = focus;
        _logTableState = logTableState;
        _settings = settings;
        _xmlResolver = xmlResolver;
        _traceLogger = traceLogger;

        _eventTableColumns.Select(s => s.Columns);
        _columnOrder.Select(s => s.ColumnOrder);
        _selection.Select(s => s.Selection);
        _focus.Select(s => s.Focus);
    }

    public async Task CopySelectedEvent(EventCopyFormat? format = null)
    {
        // Copy is best-effort: callers are typically Blazor handlers that don't catch, so a failure would surface as an
        // unhandled UI exception. Log and swallow to preserve fire-and-forget behavior.
        try
        {
            string stringToCopy = await GetFormattedEvent(format).ConfigureAwait(false);

            await MauiClipboard.SetTextAsync(stringToCopy).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _traceLogger.Error($"{nameof(MauiClipboardService)}.{nameof(CopySelectedEvent)}: failed: {ex}");
        }
    }

    public async Task CopyTextAsync(string text)
    {
        // Same best-effort contract as CopySelectedEvent: callers are typically fire-and-forget UI handlers.
        try
        {
            await MauiClipboard.SetTextAsync(text).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _traceLogger.Error($"{nameof(MauiClipboardService)}.{nameof(CopyTextAsync)}: failed: {ex}");
        }
    }

    private static string EscapeMarkdownCell(string? value) =>
        (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal);

    private static string FormatXmlForCopy(string xml)
    {
        try
        {
            return XElement.Parse(xml).ToString();
        }
        catch (XmlException)
        {
            return xml;
        }
    }

    private static string GetLogShortName(string owningLog) =>
        owningLog[(owningLog.LastIndexOf('\\') + 1)..];

    private static ResolvedEvent? ResolveEntry(SelectionEntry? entry, LogTableState logTable)
    {
        if (entry?.CurrentHandle is not { } handle) { return null; }

        return logTable.EventsForLog(handle.LogId).TryGetDetail(handle, out var detail) ? detail : null;
    }

    private void AppendFormattedEvent(
        StringBuilder builder,
        EventCopyFormat format,
        ResolvedEvent @event,
        string xml)
    {
        switch (format)
        {
            case EventCopyFormat.Default:
                foreach ((ColumnName column, _) in _eventTableColumns.Value.Where(x => x.Value))
                {
                    switch (column)
                    {
                        case ColumnName.RecordId:
                            builder.Append($"\"{@event.RecordId}\" ");
                            break;
                        case ColumnName.Level:
                            builder.Append($"\"{@event.Level}\" ");
                            break;
                        case ColumnName.DateAndTime:
                            builder.Append($"\"{@event.TimeCreated.ConvertTimeZone(_settings.TimeZoneInfo)}\" ");
                            break;
                        case ColumnName.ActivityId:
                            builder.Append($"\"{@event.ActivityId}\" ");
                            break;
                        case ColumnName.Log:
                            builder.Append($"\"{GetLogShortName(@event.OwningLog)}\" ");
                            break;
                        case ColumnName.ComputerName:
                            builder.Append($"\"{@event.ComputerName}\" ");
                            break;
                        case ColumnName.Source:
                            builder.Append($"\"{@event.Source}\" ");
                            break;
                        case ColumnName.EventId:
                            builder.Append($"\"{@event.Id}\" ");
                            break;
                        case ColumnName.TaskCategory:
                            builder.Append($"\"{@event.TaskCategory}\" ");
                            break;
                        case ColumnName.Keywords:
                            builder.Append($"\"{@event.KeywordsDisplayName}\" ");
                            break;
                        case ColumnName.ProcessId:
                            builder.Append($"\"{@event.ProcessId}\" ");
                            break;
                        case ColumnName.ThreadId:
                            builder.Append($"\"{@event.ThreadId}\" ");
                            break;
                        case ColumnName.User:
                            builder.Append($"\"{@event.UserId}\" ");
                            break;
                    }
                }

                builder.Append($"\"{@event.Description}\"");
                break;
            case EventCopyFormat.Simple:
                builder.Append($"\"{@event.Level}\" ");
                builder.Append($"\"{@event.TimeCreated.ConvertTimeZone(_settings.TimeZoneInfo)}\" ");
                builder.Append($"\"{@event.Source}\" ");
                builder.Append($"\"{@event.Id}\" ");
                builder.Append($"\"{@event.Description}\"");
                break;
            case EventCopyFormat.Xml:
                if (!string.IsNullOrEmpty(xml)) { builder.Append(FormatXmlForCopy(xml)); }

                break;
            case EventCopyFormat.Full:
            default:
                builder.AppendLine($"Log Name: {@event.LogName}");
                builder.AppendLine($"Source: {@event.Source}");
                builder.AppendLine($"Date: {@event.TimeCreated.ConvertTimeZone(_settings.TimeZoneInfo)}");
                builder.AppendLine($"Event ID: {@event.Id}");
                builder.AppendLine($"Task Category: {@event.TaskCategory}");
                builder.AppendLine($"Level: {@event.Level}");
                builder.AppendLine($"Keywords: {@event.KeywordsDisplayName}");
                builder.AppendLine($"User: {@event.UserId}");
                builder.AppendLine($"Computer: {@event.ComputerName}");
                builder.AppendLine("Description:");
                builder.AppendLine(@event.Description);
                builder.AppendLine("Event Xml:");

                if (!string.IsNullOrEmpty(xml))
                {
                    builder.AppendLine(FormatXmlForCopy(xml));
                }

                break;
        }
    }

    private string BuildMarkdownTable(IReadOnlyList<ResolvedEvent> events)
    {
        var enabled = _eventTableColumns.Value;
        var order = _columnOrder.Value;
        var columns = (order.IsEmpty
                ? enabled.Where(column => column.Value).Select(column => column.Key).OrderBy(column => column)
                : order.Where(column => enabled.TryGetValue(column, out bool isEnabled) && isEnabled))
            .ToList();

        StringBuilder builder = new();

        builder.Append("| ");
        foreach (var column in columns) { builder.Append(EscapeMarkdownCell(column.ToFullString())).Append(" | "); }

        builder.AppendLine("Description |");

        builder.Append('|');
        for (int separator = 0; separator <= columns.Count; separator++) { builder.Append(" --- |"); }

        builder.AppendLine();

        foreach (var @event in events)
        {
            builder.Append("| ");
            foreach (var column in columns) { builder.Append(EscapeMarkdownCell(GetColumnText(column, @event))).Append(" | "); }

            builder.Append(EscapeMarkdownCell(@event.Description)).AppendLine(" |");
        }

        return builder.ToString().TrimEnd();
    }

    private string FormatEventForCopy(EventCopyFormat format, ResolvedEvent @event, string xml)
    {
        if (format == EventCopyFormat.Xml)
        {
            return string.IsNullOrEmpty(xml) ? string.Empty : FormatXmlForCopy(xml);
        }

        StringBuilder builder = new();

        AppendFormattedEvent(builder, format, @event, xml);

        return builder.ToString();
    }

    private string GetColumnText(ColumnName column, ResolvedEvent @event) =>
        EventTableColumnFormatter.GetCellText(@event, column, _settings.TimeZoneInfo);

    private async Task<string> GetFormattedEvent(EventCopyFormat? format)
    {
        // Snapshot the selection once: re-reading across awaits could see a changed or empty list mid-resolve, copying
        // the wrong event or throwing. Entries that no longer resolve are dropped.
        var logTable = _logTableState.Value;
        var events = ResolveSelection(_selection.Value, logTable);
        var selected = ResolveEntry(_focus.Value, logTable);

        var resolvedFormat = format ?? _settings.CopyFormat;

        if (resolvedFormat == EventCopyFormat.Markdown)
        {
            IReadOnlyList<ResolvedEvent> markdownEvents =
                events.Count == 0 ? (selected is null ? [] : [selected]) : events;

            return markdownEvents.Count == 0 ? string.Empty : BuildMarkdownTable(markdownEvents);
        }

        bool needsXml = resolvedFormat is EventCopyFormat.Xml or EventCopyFormat.Full;

        // Selection empty: fall back to the focused entry so a right-click that didn't reach the table still copies the
        // focused event.
        if (events.Count == 0)
        {
            if (selected is null) { return string.Empty; }

            string xml = needsXml ? await _xmlResolver.GetXmlAsync(selected) : string.Empty;

            return FormatEventForCopy(resolvedFormat, selected, xml);
        }

        if (events.Count == 1)
        {
            // Copy the single selected entry (events[0]), not the focused row, so Ctrl+C copies what's selected even
            // when the focus cursor moved off (e.g., Ctrl+click toggled the only selected row off but left focus on it).
            string xml = needsXml ? await _xmlResolver.GetXmlAsync(events[0]) : string.Empty;

            return FormatEventForCopy(resolvedFormat, events[0], xml);
        }

        string[] xmlByIndex;

        if (needsXml)
        {
            int maxConcurrency = Math.Max(2, Math.Min(events.Count, Environment.ProcessorCount));
            using var resolverLock = new SemaphoreSlim(maxConcurrency, maxConcurrency);

            var resolveTasks = new Task<string>[events.Count];

            for (int i = 0; i < events.Count; i++)
            {
                var evt = events[i];

                resolveTasks[i] = ResolveXmlAsync(evt, resolverLock);
            }

            xmlByIndex = await Task.WhenAll(resolveTasks);
        }
        else
        {
            xmlByIndex = [];
        }

        StringBuilder stringToCopy = new();

        for (int i = 0; i < events.Count; i++)
        {
            string xml = needsXml ? xmlByIndex[i] : string.Empty;

            AppendFormattedEvent(stringToCopy, resolvedFormat, events[i], xml);
            stringToCopy.AppendLine();
        }

        return stringToCopy.ToString();
    }

    private IReadOnlyList<ResolvedEvent> ResolveSelection(ImmutableList<SelectionEntry> selection, LogTableState logTable)
    {
        if (selection.Count == 0) { return []; }

        var resolved = new List<ResolvedEvent>(selection.Count);

        foreach (var entry in selection)
        {
            if (ResolveEntry(entry, logTable) is { } detail) { resolved.Add(detail); }
        }

        return resolved;
    }

    private async Task<string> ResolveXmlAsync(ResolvedEvent evt, SemaphoreSlim resolverLock)
    {
        await resolverLock.WaitAsync().ConfigureAwait(false);

        try
        {
            return await _xmlResolver.GetXmlAsync(evt).ConfigureAwait(false);
        }
        finally
        {
            resolverLock.Release();
        }
    }
}
