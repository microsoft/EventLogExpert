// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Logging;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.UI;
using EventLogExpert.UI.Common.Clipboard;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.EventTable;
using Fluxor;
using System.Collections.Immutable;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace EventLogExpert.Services;

public sealed class ClipboardService : IClipboardService
{
    private readonly IStateSelection<EventTableState, ImmutableDictionary<ColumnName, bool>> _eventTableColumns;
    private readonly IStateSelection<EventLogState, ResolvedEvent?> _selectedEvent;
    private readonly IStateSelection<EventLogState, ImmutableList<ResolvedEvent>> _selectedEvents;
    private readonly ISettingsService _settings;
    private readonly ITraceLogger _traceLogger;
    private readonly IEventXmlResolver _xmlResolver;

    public ClipboardService(
        IStateSelection<EventTableState, ImmutableDictionary<ColumnName, bool>> eventTableColumns,
        IStateSelection<EventLogState, ImmutableList<ResolvedEvent>> selectedEvents,
        IStateSelection<EventLogState, ResolvedEvent?> selectedEvent,
        ISettingsService settings,
        IEventXmlResolver xmlResolver,
        ITraceLogger traceLogger)
    {
        _eventTableColumns = eventTableColumns;
        _selectedEvents = selectedEvents;
        _selectedEvent = selectedEvent;
        _settings = settings;
        _xmlResolver = xmlResolver;
        _traceLogger = traceLogger;

        _eventTableColumns.Select(s => s.Columns);
        _selectedEvents.Select(s => s.SelectedEvents);
        _selectedEvent.Select(s => s.SelectedEvent);
    }

    public async Task CopySelectedEvent(EventCopyFormat? format = null)
    {
        // Copy is best-effort: most callers are Blazor event handlers that don't catch, so any
        // failure (clipboard unavailable, XML parse, resolver fault) would surface as an
        // unhandled UI exception. Log and swallow to preserve previous fire-and-forget behavior.
        try
        {
            string stringToCopy = await GetFormattedEvent(format).ConfigureAwait(false);

            await Clipboard.SetTextAsync(stringToCopy).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _traceLogger.Error($"{nameof(ClipboardService)}.{nameof(CopySelectedEvent)}: failed: {ex}");
        }
    }

    public async Task CopyTextAsync(string text)
    {
        // Same best-effort contract as CopySelectedEvent: callers (banner copy-details, filter
        // export, future surfaces) are typically fire-and-forget UI handlers.
        try
        {
            await Clipboard.SetTextAsync(text).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _traceLogger.Error($"{nameof(ClipboardService)}.{nameof(CopyTextAsync)}: failed: {ex}");
        }
    }

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

    private async Task<string> GetFormattedEvent(EventCopyFormat? format)
    {
        // Snapshot the selection once. Re-reading _selectedEvents.Value across awaits could see
        // a different (or empty) list if selection changes mid-resolve, leading to copying the
        // wrong event or an IndexOutOfRangeException.
        var events = _selectedEvents.Value;
        var selected = _selectedEvent.Value;

        var resolvedFormat = format ?? _settings.CopyFormat;
        bool needsXml = resolvedFormat is EventCopyFormat.Xml or EventCopyFormat.Full;

        // Single-event copy: prefer the selected (focused) row so right-click → copy
        // targets the focused event. Fall back to the only selected event when selected
        // is null (e.g., right-click that didn't reach the table). When selection is
        // empty but selected is set, copy selected so the context menu still works.
        if (events.IsEmpty)
        {
            if (selected is null) { return string.Empty; }

            string xml = needsXml ? await _xmlResolver.GetXmlAsync(selected) : string.Empty;

            return FormatEventForCopy(resolvedFormat, selected, xml);
        }

        if (events.Count == 1)
        {
            // Use the actual selected entry — not the focused row — so keyboard
            // Ctrl+C copies what's selected, even when the focus cursor has
            // moved off (e.g., Ctrl+click toggled the only selected row off
            // but left focus on it). The context-menu/right-click flow keeps
            // SelectedEvents in sync with the right-clicked row, so this still
            // copies the focused row in that path.
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
