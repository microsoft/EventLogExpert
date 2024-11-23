// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Providers;
using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Principal;
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

    protected readonly IEventResolverCache? cache;
    protected readonly ITraceLogger? logger;
    protected readonly ConcurrentDictionary<string, ProviderDetails?> providerDetails = new();
    protected readonly ReaderWriterLockSlim providerDetailsLock = new();

    private readonly Regex _sectionsToReplace = WildcardWithNumberRegex();

    protected EventResolverBase(IEventResolverCache? cache = null, ITraceLogger? logger = null)
    {
        this.cache = cache;
        this.logger = logger;
    }

    public DisplayEventModel ResolveEvent(EventRecord eventRecord) =>
        new(eventRecord.PathName, eventRecord.PathType)
        {
            ActivityId = eventRecord.ActivityId,
            ComputerName = cache?.GetOrAddValue(eventRecord.ComputerName) ?? eventRecord.ComputerName,
            Description = ResolveDescription(eventRecord),
            Id = eventRecord.Id,
            KeywordsDisplayNames = GetKeywordsFromBitmask(eventRecord),
            Level = Severity.GetString(eventRecord.Level),
            LogName = cache?.GetOrAddValue(eventRecord.LogName) ?? eventRecord.LogName,
            ProcessId = eventRecord.ProcessId,
            RecordId = eventRecord.RecordId,
            Source = cache?.GetOrAddValue(eventRecord.ProviderName) ?? eventRecord.ProviderName,
            TaskCategory = ResolveTaskName(eventRecord),
            ThreadId = eventRecord.ThreadId,
            TimeCreated = eventRecord.TimeCreated,
            UserId = eventRecord.UserId,
            Xml = eventRecord.Xml ?? string.Empty
        };

    private static ReadOnlySpan<char> CleanupFormatting(string unformattedString)
    {
        Span<char> buffer = stackalloc char[unformattedString.Length * 2];
        int bufferIndex = 0;

        for (int i = 0; i < unformattedString.Length; i++)
        {
            switch (unformattedString[i])
            {
                case '%' when i + 1 < unformattedString.Length:
                    switch (unformattedString[i + 1])
                    {
                        case 'n':
                            if (unformattedString[i + 2] != '\r')
                            {
                                buffer[bufferIndex++] = '\r';
                                buffer[bufferIndex++] = '\n';
                            }

                            i++;

                            break;
                        case 't':
                            buffer[bufferIndex++] = '\t';
                            i++;

                            break;
                        default:
                            buffer[bufferIndex++] = unformattedString[i];

                            break;
                    }

                    break;
                case '\r' when i + 1 < unformattedString.Length && unformattedString[i + 1] != '\n':
                    buffer[bufferIndex++] = '\r';
                    buffer[bufferIndex++] = '\n';

                    break;
                case '\0':
                case '\r' when i + 1 >= unformattedString.Length:
                case '\r' when i + 3 >= unformattedString.Length && unformattedString[i + 1] == '\n':
                case '\r' when i + 5 >= unformattedString.Length && unformattedString[i + 2] == '\r':
                    i++;

                    break;
                case '\r' when i + 3 < unformattedString.Length && unformattedString[i + 2] == '%' && unformattedString[i + 3] == 'n':
                    buffer[bufferIndex++] = '\r';
                    buffer[bufferIndex++] = '\n';
                    i += 3;

                    break;
                default:
                    buffer[bufferIndex++] = unformattedString[i];

                    break;
            }
        }

        return new ReadOnlySpan<char>(buffer[..bufferIndex].ToArray());
    }

    private static List<string> GetFormattedProperties(string? template, IEnumerable<object> properties)
    {
        string[]? dataNodes = null;
        List<string> providers = [];

        if (!string.IsNullOrEmpty(template) && !s_formattedPropertiesCache.TryGetValue(template, out dataNodes))
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
            string? outType = index < dataNodes?.Length ? dataNodes[index] : null;

            switch (property)
            {
                case DateTime eventTime:
                    providers.Add(eventTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffff00K"));

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

    private static EventModel? GetModernEvent(EventRecord eventRecord, ProviderDetails details)
    {
        if (eventRecord is { Version: null, LogName: null })
        {
            return null;
        }

        EventModel? modernEvent = details.Events
            .FirstOrDefault(e => e.Id == eventRecord.Id &&
                e.Version == eventRecord.Version &&
                e.LogName == eventRecord.LogName);

        if (modernEvent is not null)
        {
            return modernEvent;
        }

        // Try again forcing the long to a short and with no log name.
        // This is needed for providers such as Microsoft-Windows-Complus
        return details.Events
            .FirstOrDefault(e => (short)e.Id == eventRecord.Id && e.Version == eventRecord.Version);
    }

    private static void ResizeBuffer(ref char[] buffer, ref Span<char> source, int sizeToAdd)
    {
        char[] newBuffer = ArrayPool<char>.Shared.Rent(source.Length + sizeToAdd);
        source.CopyTo(newBuffer);
        ArrayPool<char>.Shared.Return(buffer);
        source = buffer = newBuffer;
    }

    [GeneratedRegex("%+[0-9]+")]
    private static partial Regex WildcardWithNumberRegex();

    private string FormatDescription(
        List<string> properties,
        string? descriptionTemplate,
        List<MessageModel> parameters)
    {
        string returnDescription;

        if (string.IsNullOrWhiteSpace(descriptionTemplate))
        {
            // If there is only one property then this is what certain EventRecords look like
            // when the entire description is a string literal, and there is no provider DLL needed.
            // Found a few providers that have their properties wrapped with \r\n for some reason
            returnDescription = properties.Count == 1 ?
                properties[0].TrimEnd('\0', '\r', '\n') :
                "Unable to resolve description, see XML for more details.";

            return cache?.GetOrAddDescription(returnDescription) ?? returnDescription;
        }

        ReadOnlySpan<char> description = CleanupFormatting(descriptionTemplate);

        if (properties.Count <= 0)
        {
            returnDescription = description.ToString();

            return cache?.GetOrAddDescription(returnDescription) ?? returnDescription;
        }

        char[] buffer = ArrayPool<char>.Shared.Rent(description.Length * 2);

        try
        {
            int currentLength = 0;
            int lastIndex = 0;
            Span<char> updatedDescription = buffer;

            foreach (var match in _sectionsToReplace.EnumerateMatches(description))
            {
                ReadOnlySpan<char> sectionToAdd = description[lastIndex..match.Index];

                if (currentLength + sectionToAdd.Length > updatedDescription.Length)
                {
                    ResizeBuffer(ref buffer, ref updatedDescription, sectionToAdd.Length);
                }

                sectionToAdd.CopyTo(updatedDescription[currentLength..]);
                currentLength += sectionToAdd.Length;

                ReadOnlySpan<char> propString = description[match.Index..(match.Index + match.Length)];

                if (!propString.StartsWith("%%") && int.TryParse(propString.Trim(['{', '}', '%']), out var propIndex))
                {
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

                if (currentLength + propString.Length > updatedDescription.Length)
                {
                    ResizeBuffer(ref buffer, ref updatedDescription, propString.Length);
                }

                propString.CopyTo(updatedDescription[currentLength..]);
                currentLength += propString.Length;
                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < description.Length)
            {
                ReadOnlySpan<char> sectionToAdd = description[lastIndex..];

                if (currentLength + sectionToAdd.Length > updatedDescription.Length)
                {
                    ResizeBuffer(ref buffer, ref updatedDescription, sectionToAdd.Length);
                }

                sectionToAdd.CopyTo(updatedDescription[currentLength..]);
                currentLength += sectionToAdd.Length;
            }

            returnDescription = new string(updatedDescription[..currentLength]);

            return cache?.GetOrAddDescription(returnDescription) ?? returnDescription;
        }
        catch (InvalidOperationException)
        {
            // If the regex fails to match, then we just return the original description.
            returnDescription = description.ToString();

            return cache?.GetOrAddDescription(returnDescription) ?? returnDescription;
        }
        catch (Exception ex)
        {
            logger?.Trace($"FormatDescription exception was caught: {ex}");

            return "Failed to resolve description, see XML for more details.";
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    private List<string> GetKeywordsFromBitmask(EventRecord eventRecord)
    {
        if (eventRecord.Keywords is null or 0) { return []; }

        List<string> returnValue = [];

        foreach (var k in s_standardKeywords.Keys)
        {
            if ((eventRecord.Keywords.Value & k) == k)
            {
                var keyword = s_standardKeywords[k].TrimEnd('\0');
                returnValue.Add(cache?.GetOrAddValue(keyword) ?? keyword);
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
                    var keyword = details.Keywords[k].TrimEnd('\0');
                    returnValue.Add(cache?.GetOrAddValue(keyword) ?? keyword);
                }
            }
        }

        return returnValue;
    }

    /// <summary>Resolve event descriptions from an event record.</summary>
    private string ResolveDescription(EventRecord eventRecord)
    {
        if (!providerDetails.TryGetValue(eventRecord.ProviderName, out var details) || details is null)
        {
            return "Description not found. No provider available.";
        }

        var @event = GetModernEvent(eventRecord, details);
        var properties = GetFormattedProperties(@event?.Template, eventRecord.Properties);

        return string.IsNullOrEmpty(@event?.Description) ?
            FormatDescription(
                properties,
                details.Messages.FirstOrDefault(m => m.ShortId == eventRecord.Id)?.Text,
                details.Parameters) :
            FormatDescription(properties, @event.Description, details.Parameters);
    }

    /// <summary>Resolve event task names from an event record.</summary>
    private string ResolveTaskName(EventRecord eventRecord)
    {
        if (!providerDetails.TryGetValue(eventRecord.ProviderName, out var details) || details is null)
        {
            return string.Empty;
        }

        var @event = GetModernEvent(eventRecord, details);

        if (@event?.Task is not null && details.Tasks.TryGetValue(@event.Task, out var taskName))
        {
            taskName = taskName.TrimEnd('\0');
            return cache?.GetOrAddValue(taskName) ?? taskName;
        }

        if (!eventRecord.Task.HasValue)
        {
            return string.Empty;
        }

        details.Tasks.TryGetValue(eventRecord.Task.Value, out taskName);

        if (taskName is not null)
        {
            taskName = taskName.TrimEnd('\0');
            return cache?.GetOrAddValue(taskName) ?? taskName;
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

        taskName = taskName.TrimEnd('\0');
        return cache?.GetOrAddValue(taskName) ?? taskName;
    }
}
