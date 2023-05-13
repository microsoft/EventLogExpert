// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Helpers;
using EventLogExpert.Library.Models;
using EventLogExpert.Library.Providers;
using System.Diagnostics.Eventing.Reader;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace EventLogExpert.Library.EventResolvers;

public class EventResolverBase
{
    protected readonly Action<string> _tracer;

    private readonly Regex _formatRegex = new("%+[0-9]+");

    protected EventResolverBase(Action<string> tracer)
    {
        _tracer = tracer ?? throw new ArgumentNullException(nameof(tracer));
    }

    protected string? FormatDescription(EventRecord eventRecord, string? descriptionTemplate)
    {
        if (descriptionTemplate == null)
        {
            return null;
        }

        string description = descriptionTemplate
            .Replace("\r\n%n", " \r\n")
            .Replace("%n\r\n", "\r\n ")
            .Replace("%n", "\r\n");
        var matches = _formatRegex.Matches(description);
        if (matches.Count > 0)
        {
            var sb = new StringBuilder();
            var lastIndex = 0;
            for (var i = 0; i < matches.Count; i++)
            {
                if (matches[i].Value.StartsWith("%%"))
                {
                    // The % is escaped, so skip it.
                    continue;
                }

                sb.Append(description.AsSpan(lastIndex, matches[i].Index - lastIndex));
                var propIndex = int.Parse(matches[i].Value.Trim(new[] { '{', '}', '%' }));
                var propValue = eventRecord.Properties[propIndex - 1].Value;
                if (propValue is DateTime)
                {
                    // Exactly match the format produced by EventRecord.FormatMessage(). I have no idea why it includes Unicode LRM marks, but it does.
                    sb.Append(((DateTime)propValue).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffff00K"));
                }
                else
                {
                    sb.Append(propValue);
                }

                lastIndex = matches[i].Index + matches[i].Length;
            }

            if (lastIndex < description.Length)
            {
                sb.Append(description.Substring(lastIndex));
            }

            description = sb.ToString();
        }

        while (description.EndsWith("\r\n"))
        {
            description = description.Remove(description.Length - "\r\n".Length);
        }

        return description;
    }

    protected string FormatXml(EventRecord record, string? template)
    {
        var sb = new StringBuilder(
            "<Event xmlns=\"http://schemas.microsoft.com/win/2004/08/events/event\">\r\n" +
            $"  <System>\r\n" +
            $"    <Provider Name=\"{record.ProviderName}\" />\r\n" +
            $"    <EventID{(record.Qualifiers.HasValue ? $" Qualifiers=\"{record.Qualifiers.Value}\"" : "")}>{record.Id}</EventID>\r\n" +
            $"    <Level>{record.Level}</Level>\r\n" +
            $"    <Task>{record.Task}</Task>\r\n" +
            $"    <Keywords>{(record.Keywords.HasValue ? ("0x" + record.Keywords.Value.ToString("X")) : "0x0")}</Keywords>\r\n" +
            $"    <TimeCreated SystemTime=\"{(record.TimeCreated.HasValue ? record.TimeCreated.Value.ToString("o") : "")}\" />\r\n" +
            $"    <EventRecordID>{record.RecordId}</EventRecordID>\r\n" +
            $"    <Channel>{record.LogName}</Channel>\r\n" +
            $"    <Computer>{record.MachineName}</Computer>\r\n" +
            $"  </System>\r\n" +
            $"  <EventData>\r\n");

        if (!string.IsNullOrEmpty(template))
        {
            var propertyNames = new List<string>();
            var index = -1;
            while (-1 < (index = template.IndexOf("name=", index + 1)))
            {
                var nameStart = index + 6;
                var nameEnd = template.IndexOf('"', nameStart);
                var name = template.Substring(nameStart, nameEnd - nameStart);
                propertyNames.Add(name);
            }

            for (var i = 0; i < record.Properties.Count; i++)
            {
                if (i >= propertyNames.Count)
                {
                    _tracer("EventResolverBase.FormatXml(): Property count exceeds template names.");
                    _tracer($"  Provider: {record.ProviderName} EventID: {record.Id} Property count: {record.Properties.Count} Name count: {propertyNames.Count}");
                    break;
                }

                sb.Append($"    <{propertyNames[i]}>{record.Properties[i].Value}</{propertyNames[i]}>\r\n");
            }
        }
        else
        {
            foreach (var p in record.Properties)
            {
                sb.Append($"    <Data>{p.Value}</Data>\r\n");
            }
        }

        sb.Append(
            "  </EventData>\r\n" +
            "</Event>");

        return sb.ToString();
    }

    protected DisplayEventModel ResolveFromProviderDetails(EventRecord eventRecord, ProviderDetails providerDetails)
    {
        string? description = null;
        string? xml = null;
        string? taskName = null;

        if (eventRecord.Version != null && eventRecord.LogName != null)
        {
            var modernEvents = providerDetails.Events?.Where(e => e.Id == eventRecord.Id && e.Version == eventRecord.Version && e.LogName == eventRecord.LogName).ToList();
            if (modernEvents != null && modernEvents.Count == 0)
            {
                // Try again forcing the long to a short and with no log name. This is needed for providers such as Microsoft-Windows-Complus
                modernEvents = providerDetails.Events?.Where(e => (short)e.Id == eventRecord.Id && e.Version == eventRecord.Version).ToList();
            }

            if (modernEvents != null && modernEvents.Any())
            {
                if (modernEvents.Count > 1)
                {
                    _tracer("Ambiguous modern event found:");
                    foreach (var modernEvent in modernEvents)
                    {
                        _tracer($"  Version: {modernEvent.Version} Id: {modernEvent.Id} LogName: {modernEvent.LogName} Description: {modernEvent.Description}");
                    }
                }

                var e = modernEvents[0];

                providerDetails.Tasks.TryGetValue(e.Task, out taskName);

                // If we don't have a description
                if (string.IsNullOrEmpty(e.Description))
                {
                    // And we have exactly one property
                    if (eventRecord.Properties.Count == 1)
                    {
                        // Return that property as the description. This is what certain EventRecords look like
                        // when the entire description is a string literal, and there is no provider DLL needed.
                        description = eventRecord.Properties[0].ToString();
                    }
                    else
                    {
                        description = "This event record is missing a description. The following information was included with the event:\n\n" +
                            string.Join("\n", eventRecord.Properties);
                    }
                }
                else
                {
                    description = FormatDescription(eventRecord, e.Description);
                }

                xml = FormatXml(eventRecord, e.Template);
            }
        }

        if (description == null)
        {
            description = providerDetails.Messages?.FirstOrDefault(m => m.ShortId == eventRecord.Id)?.Text;
            description = FormatDescription(eventRecord, description);
            xml = FormatXml(eventRecord, null);
        }

        if (taskName == null && eventRecord.Task.HasValue)
        {
            providerDetails.Tasks.TryGetValue(eventRecord.Task.Value, out taskName);
            if (taskName == null)
            {
                var potentialTaskNames = providerDetails.Messages?.Where(m => m.ShortId == eventRecord.Task && m.LogLink != null && m.LogLink == eventRecord.LogName).ToList();
                if (potentialTaskNames != null && potentialTaskNames.Any())
                {
                    taskName = potentialTaskNames[0].Text;

                    if (potentialTaskNames.Count > 1)
                    {
                        _tracer("More than one matching task ID was found.");
                        _tracer($"  eventRecord.Task: {eventRecord.Task}");
                        _tracer("   Potential matches:");
                        potentialTaskNames.ForEach(t => _tracer($"    {t.LogLink} {t.Text}"));
                    }
                }
                else
                {
                    taskName = eventRecord.Task == null | eventRecord.Task == 0 ? "None" : $"({eventRecord.Task})";
                }
            }
        }

        return new DisplayEventModel(
                eventRecord.RecordId,
                eventRecord.TimeCreated,
                eventRecord.Id,
                eventRecord.MachineName,
                (SeverityLevel?)eventRecord.Level,
                eventRecord.ProviderName,
                taskName,
                description,
                xml);
    }
}
