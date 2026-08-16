// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Runtime.Common.Display;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.LogTable;
using System.Collections.Immutable;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace EventLogExpert.Runtime.Common.Clipboard;

internal sealed class EventCopyFormatter(IEventDetailResolver detailResolver, IEventXmlResolver xmlResolver)
    : IEventCopyFormatter
{
    private readonly IEventDetailResolver _detailResolver = detailResolver;
    private readonly IEventXmlResolver _xmlResolver = xmlResolver;

    public async Task<string> FormatAsync(EventCopyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var events = ResolveSelection(request.Selection);
        var selected = ResolveEntry(request.Focus);

        EventCopyFormat format = request.Format;

        if (format == EventCopyFormat.Markdown)
        {
            IReadOnlyList<ResolvedEvent> markdownEvents =
                events.Count == 0 ? (selected is null ? [] : [selected]) : events;

            return markdownEvents.Count == 0 ? string.Empty : BuildMarkdownTable(markdownEvents, request);
        }

        bool needsXml = format is EventCopyFormat.Xml or EventCopyFormat.Full;

        if (events.Count == 0)
        {
            if (selected is null) { return string.Empty; }

            string xml = needsXml ? await _xmlResolver.GetXmlAsync(selected, cancellationToken).ConfigureAwait(false) : string.Empty;

            return FormatEventForCopy(format, selected, xml, request);
        }

        if (events.Count == 1)
        {
            string xml = needsXml ? await _xmlResolver.GetXmlAsync(events[0], cancellationToken).ConfigureAwait(false) : string.Empty;

            return FormatEventForCopy(format, events[0], xml, request);
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

                resolveTasks[i] = ResolveXmlAsync(evt, resolverLock, cancellationToken);
            }

            xmlByIndex = await Task.WhenAll(resolveTasks).ConfigureAwait(false);
        }
        else
        {
            xmlByIndex = [];
        }

        StringBuilder stringToCopy = new();

        for (int i = 0; i < events.Count; i++)
        {
            string xml = needsXml ? xmlByIndex[i] : string.Empty;

            AppendFormattedEvent(stringToCopy, format, events[i], xml, request);
            stringToCopy.AppendLine();
        }

        return stringToCopy.ToString();
    }

    private static void AppendFormattedEvent(
        StringBuilder builder,
        EventCopyFormat format,
        ResolvedEvent @event,
        string xml,
        EventCopyRequest request)
    {
        switch (format)
        {
            case EventCopyFormat.Default:
                foreach ((ColumnName column, _) in request.EnabledColumns.Where(x => x.Value))
                {
                    builder.Append(
                        $"\"{ColumnDescriptors.GetCellText(@event, column, new ColumnFormatContext(request.TimeZone))}\" ");
                }

                builder.Append($"\"{@event.Description}\"");
                break;
            case EventCopyFormat.Simple:
                builder.Append($"\"{@event.Level}\" ");
                builder.Append($"\"{@event.TimeCreated.ConvertTimeZone(request.TimeZone)}\" ");
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
                builder.AppendLine($"Date: {@event.TimeCreated.ConvertTimeZone(request.TimeZone)}");
                builder.AppendLine($"Event ID: {@event.Id}");
                builder.AppendLine($"Task Category: {@event.TaskCategory}");
                builder.AppendLine($"Level: {@event.Level}");
                builder.AppendLine($"Keywords: {@event.KeywordsDisplayName}");
                builder.AppendLine($"User: {@event.UserDisplayName}");

                if (@event.UserId is { } userId && !string.Equals(userId.Value, @event.UserDisplayName, StringComparison.Ordinal))
                {
                    builder.AppendLine($"User SID: {userId.Value}");
                }

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

    private static string BuildMarkdownTable(IReadOnlyList<ResolvedEvent> events, EventCopyRequest request)
    {
        var enabled = request.EnabledColumns;
        var order = request.ColumnOrder;
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
            foreach (var column in columns) { builder.Append(EscapeMarkdownCell(GetColumnText(column, @event, request.TimeZone))).Append(" | "); }

            builder.Append(EscapeMarkdownCell(@event.Description)).AppendLine(" |");
        }

        return builder.ToString().TrimEnd();
    }

    private static string EscapeMarkdownCell(string? value) =>
        (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal);

    private static string FormatEventForCopy(EventCopyFormat format, ResolvedEvent @event, string xml, EventCopyRequest request)
    {
        if (format == EventCopyFormat.Xml)
        {
            return string.IsNullOrEmpty(xml) ? string.Empty : FormatXmlForCopy(xml);
        }

        StringBuilder builder = new();

        AppendFormattedEvent(builder, format, @event, xml, request);

        return builder.ToString();
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

    private static string GetColumnText(ColumnName column, ResolvedEvent @event, TimeZoneInfo timeZone) =>
        EventTableColumnFormatter.GetCellText(@event, column, timeZone);

    private ResolvedEvent? ResolveEntry(SelectionEntry? entry)
    {
        if (entry?.CurrentHandle is not { } handle) { return null; }

        return _detailResolver.TryResolve(handle, out var detail) ? detail : null;
    }

    private IReadOnlyList<ResolvedEvent> ResolveSelection(ImmutableList<SelectionEntry> selection)
    {
        if (selection.Count == 0) { return []; }

        var resolved = new List<ResolvedEvent>(selection.Count);

        foreach (var entry in selection)
        {
            if (ResolveEntry(entry) is { } detail) { resolved.Add(detail); }
        }

        return resolved;
    }

    private async Task<string> ResolveXmlAsync(ResolvedEvent evt, SemaphoreSlim resolverLock, CancellationToken cancellationToken)
    {
        await resolverLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await _xmlResolver.GetXmlAsync(evt, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            resolverLock.Release();
        }
    }
}
