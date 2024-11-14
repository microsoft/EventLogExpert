// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Providers;
using System.Collections.Concurrent;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace EventLogExpert.Eventing.EventResolvers;

public partial class EventResolverBase
{
    /// <summary>
    ///     The mappings from the outType attribute in the EventModel XML template to determine if it should be displayed
    ///     as Hex.
    /// </summary>
    private static readonly List<string> s_displayAsHexTypes =
    [
        "win:HexInt32",
        "win:HexInt64",
        "win:Pointer",
        "win:Win32Error"
    ];

    private static readonly ConcurrentDictionary<string, string[]> s_formattedPropertiesCache = [];

    /// <summary>
    ///     These are already defined in System.Diagnostics.Eventing.Reader.StandardEventKeywords. However, the names
    ///     there do not match what is normally displayed in Event Viewer. We redefine them here so we can use our own strings.
    /// </summary>
    private static readonly Dictionary<long, string> s_standardKeywords = new()
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

    protected readonly ConcurrentDictionary<string, ProviderDetails?> providerDetails = new();
    protected readonly ReaderWriterLockSlim providerDetailsLock = new();
    protected readonly ITraceLogger? logger;

    private readonly Regex _sectionsToReplace = WildcardWithNumberRegex();

    protected EventResolverBase(ITraceLogger? logger = null)
    {
        this.logger = logger;
    }

    public IEnumerable<string> GetKeywordsFromBitmask(EventRecord eventRecord)
    {
        if (eventRecord.Keywords is null or 0) { return []; }

        List<string> returnValue = [];

        foreach (var k in s_standardKeywords.Keys)
        {
            if ((eventRecord.Keywords.Value & k) == k)
            {
                returnValue.Add(s_standardKeywords[k].TrimEnd('\0'));
            }
        }

        if (!providerDetails.TryGetValue(eventRecord.ProviderName, out var details) || details is null)
        {
            return returnValue;
        }

        // Some providers re-define the standard keywords in their own metadata,
        // so let's skip those.
        var lower32 = eventRecord.Keywords.Value & 0xFFFFFFFF;

        if (lower32 != 0)
        {
            foreach (var k in details.Keywords.Keys)
            {
                if ((lower32 & k) == k)
                {
                    returnValue.Add(details.Keywords[k].TrimEnd('\0'));
                }
            }
        }

        return returnValue;
    }

    /// <summary>Resolve event descriptions from an event record.</summary>
    public string ResolveDescription(EventRecord eventRecord)
    {
        if (!providerDetails.TryGetValue(eventRecord.ProviderName, out var details) || details is null)
        {
            return "Description not found. No provider available.";
        }

        var @event = GetModernEvent(eventRecord, details);
        var properties = GetFormattedProperties(@event?.Template, eventRecord.Properties);

        return string.IsNullOrEmpty(@event?.Description)
            ? FormatDescription(
                properties,
                details.Messages.FirstOrDefault(m => m.ShortId == eventRecord.Id)?.Text,
                details.Parameters)
            : FormatDescription(properties, @event.Description, details.Parameters);
    }

    /// <summary>Resolve event task names from an event record.</summary>
    public string ResolveTaskName(EventRecord eventRecord)
    {
        if (!providerDetails.TryGetValue(eventRecord.ProviderName, out var details) || details is null)
        {
            return string.Empty;
        }

        var @event = GetModernEvent(eventRecord, details);

        if (@event?.Task is not null && details.Tasks.TryGetValue(@event.Task, out var taskName))
        {
            return taskName.TrimEnd('\0');
        }

        if (!eventRecord.Task.HasValue)
        {
            return string.Empty;
        }

        details.Tasks.TryGetValue(eventRecord.Task.Value, out taskName);

        if (taskName is not null)
        {
            return taskName.TrimEnd('\0');
        }

        var potentialTaskNames = details.Messages
            .Where(m => m.ShortId == eventRecord.Task && m.LogLink != null && m.LogLink == eventRecord.LogName)
            .ToList();

        if (potentialTaskNames is { Count: > 0 })
        {
            taskName = potentialTaskNames[0].Text;

            if (potentialTaskNames.Count > 1)
            {
                logger?.Trace("More than one matching task ID was found.");
                logger?.Trace($"  eventRecord.Task: {eventRecord.Task}");
                logger?.Trace("   Potential matches:");

                potentialTaskNames.ForEach(t => logger?.Trace($"    {t.LogLink} {t.Text}"));
            }
        }
        else
        {
            taskName = (eventRecord.Task == null) | (eventRecord.Task == 0) ? "None" : $"({eventRecord.Task})";
        }

        return taskName.TrimEnd('\0');
    }

    protected string FormatDescription(
        List<string> properties,
        string? descriptionTemplate,
        List<MessageModel> parameters)
    {
        if (string.IsNullOrWhiteSpace(descriptionTemplate))
        {
            // If there is only one property then this is what certain EventRecords look like
            // when the entire description is a string literal, and there is no provider DLL needed.
            // Found a few providers that have their properties wrapped with \r\n for some reason
            return properties.Count == 1 ?
                properties[0].Trim(['\0', '\r', '\n']) :
                "Unable to resolve description, see XML for more details.";
        }

        ReadOnlySpan<char> description = descriptionTemplate
            .Replace("\r\n%n", " \r\n")
            .Replace("%n\r\n", "\r\n ")
            .Replace("%n", "\r\n");

        try
        {
            StringBuilder updatedDescription = new();
            int lastIndex = 0;

            foreach (var match in _sectionsToReplace.EnumerateMatches(description))
            {
                updatedDescription.Append(description[lastIndex..match.Index]);

                ReadOnlySpan<char> propString = description[match.Index..(match.Index + match.Length)];

                if (!propString.StartsWith("%%"))
                {
                    var propIndex = int.Parse(propString.Trim(['{', '}', '%']));

                    if (propIndex - 1 >= properties.Count)
                    {
                        return "Unable to resolve description, see XML for more details.";
                    }

                    propString = properties[propIndex - 1];
                }

                if (propString.StartsWith("%%") && parameters.Count > 0)
                {
                    int endParameterId = propString.IndexOf(' ');
                    var parameterIdString = endParameterId > 2 ? propString.Slice(2, endParameterId) : propString[2..];

                    if (long.TryParse(parameterIdString, out long parameterId))
                    {
                        // Some parameters exceed int size and need to be cast from long to int
                        // because they are actually negative numbers
                        ReadOnlySpan<char> parameterMessage =
                            parameters.FirstOrDefault(m => m.RawId == (int)parameterId)?.Text ?? string.Empty;

                        if (!parameterMessage.IsEmpty)
                        {
                            // Some RawId parameters have a trailing '%0' that needs to be removed
                            propString = endParameterId > 2 ?
                                string.Concat(parameterMessage.TrimEnd(['%', '0']), propString[++endParameterId..]) :
                                parameterMessage.TrimEnd(['%', '0']);
                        }
                    }
                }

                updatedDescription.Append(propString);

                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < description.Length)
            {
                updatedDescription.Append(description[lastIndex..].TrimEnd(['\0', '\r', '\n']));
            }

            return updatedDescription.ToString();
        }
        catch (InvalidOperationException)
        {
            // If the regex fails to match, then we just return the original description.
            return description.TrimEnd(['\0', '\r', '\n']).ToString();
        }
        catch (Exception ex)
        {
            logger?.Trace($"FormatDescription exception was caught: {ex}");

            return "Failed to resolve description, see XML for more details.";
        }
    }

    private static List<string> GetFormattedProperties(string? template, IEnumerable<object> properties)
    {
        string[]? dataNodes = null;
        List<string> providers = [];

        if (!string.IsNullOrWhiteSpace(template) && !s_formattedPropertiesCache.TryGetValue(template, out dataNodes))
        {
            dataNodes = XElement.Parse(template)
                .Descendants()
                .Attributes()
                .Where(a => a.Name == "outType")
                .Select(a => a.Value)
                .ToArray();

            s_formattedPropertiesCache.TryAdd(template, dataNodes);
        }

        int index = 0;

        foreach (object property in properties)
        {
            string? outType = dataNodes?[index];

            switch (property)
            {
                case DateTime eventTime:
                    providers.Add(eventTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffff00K"));

                    break;
                case byte[] bytes:
                    providers.Add(Convert.ToHexString(bytes));

                    break;
                case SecurityIdentifier sid:
                    providers.Add(sid.Value);

                    break;
                default:
                    if (string.IsNullOrEmpty(outType))
                    {
                        providers.Add($"{property}");
                    }
                    else if (s_displayAsHexTypes.Contains(outType, StringComparer.OrdinalIgnoreCase))
                    {
                        providers.Add($"0x{property:X}");
                    }
                    else if (string.Equals(outType, "win:HResult", StringComparison.OrdinalIgnoreCase) && property is int hResult)
                    {
                        providers.Add(ResolverMethods.GetErrorMessage((uint)hResult));
                    }
                    else
                    {
                        providers.Add($"{property}");
                    }

                    break;
            }

            index++;
        }

        return providers;
    }

    [GeneratedRegex("%+[0-9]+")]
    private static partial Regex WildcardWithNumberRegex();

    private EventModel? GetModernEvent(EventRecord eventRecord, ProviderDetails details)
    {
        if (eventRecord is { Version: null, LogName: null })
        {
            return null;
        }

        var modernEvents = details.Events
            .Where(e => e.Id == eventRecord.Id &&
                e.Version == eventRecord.Version &&
                e.LogName == eventRecord.LogName).ToList();

        if (modernEvents is { Count: 0 })
        {
            // Try again forcing the long to a short and with no log name.
            // This is needed for providers such as Microsoft-Windows-Complus
            modernEvents = details.Events
                .Where(e => (short)e.Id == eventRecord.Id && e.Version == eventRecord.Version).ToList();
        }

        if (modernEvents is not { Count: > 0 })
        {
            return null;
        }

        if (modernEvents.Count <= 1)
        {
            return modernEvents[0];
        }

        logger?.Trace("Ambiguous modern event found:");

        foreach (var modernEvent in modernEvents)
        {
            logger?.Trace($"  Version: {modernEvent.Version} Id: {modernEvent.Id} " +
                $"LogName: {modernEvent.LogName} Description: {modernEvent.Description}");
        }

        return modernEvents[0];
    }
}
