﻿// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Providers;
using Microsoft.Extensions.Logging;
using System.Diagnostics.Eventing.Reader;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace EventLogExpert.Eventing.EventResolvers;

public partial class EventResolverBase
{
    /// <summary>
    ///     These are already defined in System.Diagnostics.Eventing.Reader.StandardEventKeywords. However, the names
    ///     there do not match what is normally displayed in Event Viewer. We redefine them here so we can use our own strings.
    /// </summary>
    private static readonly Dictionary<long, string> StandardKeywords = new()
    {
        { 0x1000000000000, "Response Time" },
        { 0x2000000000000, "Wdi Context" },
        { 0x4000000000000, "Wdi Diag" },
        { 0x8000000000000, "Sqm" },
        { 0x10000000000000, "Audit Failure" },
        { 0x20000000000000, "Audit Success" },
        { 0x40000000000000, "Correlation Hint" },
        { 0x80000000000000, "Classic" }
    };

    /// <summary>
    ///     The mappings from the outType attribute in the EventModel XML template
    ///     to determine if it should be displayed as Hex.
    /// </summary>
    private static readonly Dictionary<string, bool> XmlHexMappings = new()
    {
        { "win:HexInt32", true },
        { "win:HexInt64", true },
        { "win:Pointer", true },
        { "win:SID", false },
        { "win:UnicodeString", false },
        { "win:Win32Error", true },
        { "xs:dateTime", false },
        { "xs:GUID", false },
        { "xs:string", false },
        { "xs:unsignedByte", true },
        { "xs:unsignedInt", false },
        { "xs:unsignedLong", false }
    };

    protected readonly Action<string, LogLevel> _tracer;

    private readonly Regex _sectionsToReplace = WildcardWithNumberRegex();

    protected EventResolverBase(Action<string, LogLevel> tracer)
    {
        _tracer = tracer ?? throw new ArgumentNullException(nameof(tracer));
    }

    protected static IEnumerable<string> GetKeywordsFromBitmask(long? bitmask, ProviderDetails? providerDetails)
    {
        if (bitmask is null or 0) { return Enumerable.Empty<string>(); }

        var returnValue = new List<string>();

        foreach (var k in StandardKeywords.Keys)
        {
            if ((bitmask.Value & k) == k) { returnValue.Add(StandardKeywords[k].TrimEnd('\0')); }
        }

        if (providerDetails != null)
        {
            // Some providers re-define the standard keywords in their own metadata,
            // so let's skip those.
            var lower32 = bitmask.Value & 0xFFFFFFFF;

            if (lower32 != 0)
            {
                foreach (var k in providerDetails.Keywords.Keys)
                {
                    if ((lower32 & k) == k) { returnValue.Add(providerDetails.Keywords[k].TrimEnd('\0')); }
                }
            }
        }

        return returnValue;
    }

    protected string? FormatDescription(
        List<string> properties,
        string? descriptionTemplate,
        List<MessageModel> parameters)
    {
        if (descriptionTemplate == null) { return null; }

        string description = descriptionTemplate
            .Replace("\r\n%n", " \r\n")
            .Replace("%n\r\n", "\r\n ")
            .Replace("%n", "\r\n");

        var matches = _sectionsToReplace.Matches(description);

        if (matches.Count <= 0)
        {
            return description.TrimEnd(['\r', '\n']);
        }

        try
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
                var propIndex = int.Parse(matches[i].Value.Trim(['{', '}', '%']));

                if (propIndex - 1 >= properties.Count) { return null; }

                var propString = properties[propIndex - 1];

                if (propString.StartsWith("%%") && parameters.Count > 0)
                {
                    var endParameterId = propString.IndexOf(' ');
                    var parameterIdString = endParameterId > 2 ? propString[2..endParameterId] : propString[2..];

                    if (int.TryParse(parameterIdString, out var parameterId))
                    {
                        var parameterMessage = parameters.FirstOrDefault(m => m.ShortId == parameterId);

                        if (parameterMessage != null)
                        {
                            propString = endParameterId > 2 ?
                                parameterMessage.Text.TrimEnd(['\r', '\n']) + propString[++endParameterId..] :
                                parameterMessage.Text.TrimEnd(['\r', '\n']);
                        }
                    }
                }

                sb.Append(propString);

                lastIndex = matches[i].Index + matches[i].Length;
            }

            if (lastIndex < description.Length)
            {
                sb.Append(description[lastIndex..]);
            }

            description = sb.ToString();
        }
        catch (Exception ex)
        {
            _tracer($"FormatDescription exception was caught: {ex}", LogLevel.Information);

            return null;
        }

        return description.TrimEnd(['\r', '\n']);
    }

    /// <summary>Resolve event descriptions from a provider.</summary>
    /// <param name="eventRecord"></param>
    /// <param name="eventProperties">
    ///     The getter for EventRecord.Properties is expensive, so we require the caller to call it
    ///     and then pass this value separately. This ensures the value can be reused by both the caller and the resolver,
    ///     optimizing performance in this critical path.
    /// </param>
    /// <param name="providerDetails"></param>
    /// <param name="owningLogName"></param>
    /// <returns></returns>
    protected DisplayEventModel ResolveFromProviderDetails(
        EventRecord eventRecord,
        IList<EventProperty> eventProperties,
        ProviderDetails providerDetails,
        string owningLogName)
    {
        string? description = null;
        string? taskName = null;

        if (eventRecord is { Version: not null, LogName: not null })
        {
            var modernEvents = providerDetails.Events
                .Where(e => e.Id == eventRecord.Id &&
                    e.Version == eventRecord.Version &&
                    e.LogName == eventRecord.LogName).ToList();

            if (modernEvents is { Count: 0 })
            {
                // Try again forcing the long to a short and with no log name.
                // This is needed for providers such as Microsoft-Windows-Complus
                modernEvents = providerDetails.Events?
                    .Where(e => (short)e.Id == eventRecord.Id && e.Version == eventRecord.Version).ToList();
            }

            if (modernEvents is { Count: > 0 })
            {
                if (modernEvents.Count > 1)
                {
                    _tracer("Ambiguous modern event found:", LogLevel.Information);

                    foreach (var modernEvent in modernEvents)
                    {
                        _tracer($"  Version: {modernEvent.Version} Id: {modernEvent.Id} " +
                            $"LogName: {modernEvent.LogName} Description: {modernEvent.Description}",
                            LogLevel.Information);
                    }
                }

                var e = modernEvents[0];

                providerDetails.Tasks.TryGetValue(e.Task, out taskName);

                var properties = GetFormattedProperties(e.Template, eventProperties);

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
                        description = "This event record is missing a description. " +
                            "The following information was included with the event:\n\n" +
                            string.Join("\n", eventRecord.Properties);
                    }
                }
                else
                {
                    description = FormatDescription(properties, e.Description, providerDetails.Parameters);
                }
            }
        }

        if (description == null)
        {
            var properties = GetFormattedProperties(null, eventProperties);
            description = FormatDescription(properties, providerDetails.Messages.FirstOrDefault(m => m.ShortId == eventRecord.Id)?.Text, providerDetails.Parameters);
        }

        if (taskName == null && eventRecord.Task.HasValue)
        {
            providerDetails.Tasks.TryGetValue(eventRecord.Task.Value, out taskName);

            if (taskName == null)
            {
                var potentialTaskNames = providerDetails.Messages
                    .Where(m => m.ShortId == eventRecord.Task && m.LogLink != null && m.LogLink == eventRecord.LogName)
                    .ToList();

                if (potentialTaskNames is { Count: > 0 })
                {
                    taskName = potentialTaskNames[0].Text;

                    if (potentialTaskNames.Count > 1)
                    {
                        _tracer("More than one matching task ID was found.", LogLevel.Information);
                        _tracer($"  eventRecord.Task: {eventRecord.Task}", LogLevel.Information);
                        _tracer("   Potential matches:", LogLevel.Information);
                        potentialTaskNames.ForEach(t => _tracer($"    {t.LogLink} {t.Text}", LogLevel.Information));
                    }
                }
                else
                {
                    taskName = (eventRecord.Task == null) | (eventRecord.Task == 0) ? "None" : $"({eventRecord.Task})";
                }
            }
        }

        return new DisplayEventModel(
            eventRecord.RecordId,
            eventRecord.ActivityId,
            eventRecord.TimeCreated!.Value.ToUniversalTime(),
            eventRecord.Id,
            eventRecord.MachineName,
            Severity.GetString(eventRecord.Level),
            eventRecord.ProviderName,
            taskName?.TrimEnd('\0') ?? string.Empty,
            description?.TrimEnd('\0') ?? "Unable to format description",
            eventRecord.Qualifiers,
            GetKeywordsFromBitmask(eventRecord.Keywords, providerDetails),
            eventRecord.ProcessId,
            eventRecord.ThreadId,
            eventRecord.UserId,
            eventRecord.LogName!,
            owningLogName,
            eventRecord.ToXml());
    }

    private static List<string> GetFormattedProperties(string? template, IList<EventProperty> properties)
    {
        List<string> providers = [];
        XmlNodeList? dataNodes = null;

        if (!string.IsNullOrWhiteSpace(template))
        {
            XmlDocument xml = new();
            xml.LoadXml(template);

            // Select all the 'data' nodes in the XML
            dataNodes = xml.SelectNodes("/*/*");
        }

        for (int i = 0; i < properties.Count; i++)
        {
            string? outType = dataNodes?[i]?.Attributes?["outType"]?.Value;

            switch (properties[i].Value)
            {
                case DateTime eventTime:
                    // Exactly match the format produced by EventRecord.FormatMessage().
                    // I have no idea why it includes Unicode LRM marks, but it does.
                    providers.Add(eventTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffff00K"));
                    continue;
                case byte[] bytes:
                    providers.Add(Convert.ToHexString(bytes));
                    continue;
                case SecurityIdentifier sid:
                    providers.Add(sid.Value);
                    continue;
                default:
                    if (!string.IsNullOrEmpty(outType) && XmlHexMappings.TryGetValue(outType, out bool isHex))
                    {
                        providers.Add(isHex ? $"0x{properties[i].Value:X}" : $"{properties[i].Value}");
                    }
                    else
                    {
                        providers.Add($"{properties[i].Value}");
                    }

                    continue;
            }
        }

        return providers;
    }

    [GeneratedRegex("%+[0-9]+")]
    private static partial Regex WildcardWithNumberRegex();
}
