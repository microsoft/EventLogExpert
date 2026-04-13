// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Providers;
using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace EventLogExpert.Eventing.EventResolvers;

public partial class EventResolverBase : IDisposable
{
    private const string DefaultFailedDescription = "Failed to resolve description, see XML for more details.";
    private const string DefaultNoMatchingDescription = "No matching message found with loaded providers, see XML for more details";
    private const string DefaultNoProviderDescription = "No matching provider available, see XML for more details.";

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

    protected readonly ConcurrentDictionary<string, ProviderDetails?> ProviderDetails = new();
    protected readonly Lock ProviderDetailsLock = new();

    private readonly IEventResolverCache? _cache;
    private readonly ITraceLogger? _logger;
    private readonly Regex _sectionsToReplace = WildcardWithNumberRegex();

    private int _disposed;

    protected EventResolverBase(IEventResolverCache? cache = null, ITraceLogger? logger = null)
    {
        _cache = cache;
        _logger = logger;
    }

    protected bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    protected ITraceLogger? Logger => _logger;

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public virtual DisplayEventModel ResolveEvent(EventRecord eventRecord)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        var keywords = GetKeywordsFromBitmask(eventRecord);

        // Resolve the modern event once and reuse for both description and task name
        ProviderDetails.TryGetValue(eventRecord.ProviderName, out var details);
        var modernEvent = details is not null ? GetModernEvent(eventRecord, details) : null;

        return new DisplayEventModel(eventRecord.PathName, eventRecord.PathType)
        {
            ActivityId = eventRecord.ActivityId,
            ComputerName = _cache?.GetOrAddValue(eventRecord.ComputerName) ?? eventRecord.ComputerName,
            Description = ResolveDescription(eventRecord, details, modernEvent),
            Id = eventRecord.Id,
            Keywords = keywords,
            KeywordsDisplayName = string.Join(", ", keywords),
            Level = Severity.GetString(eventRecord.Level),
            LogName = _cache?.GetOrAddValue(eventRecord.LogName) ?? eventRecord.LogName,
            ProcessId = eventRecord.ProcessId,
            RecordId = eventRecord.RecordId,
            Source = _cache?.GetOrAddValue(eventRecord.ProviderName) ?? eventRecord.ProviderName,
            TaskCategory = ResolveTaskName(eventRecord, details, modernEvent),
            ThreadId = eventRecord.ThreadId,
            TimeCreated = eventRecord.TimeCreated,
            UserId = eventRecord.UserId,
            Xml = eventRecord.Xml ?? string.Empty
        };
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing) { return; }

        // Use Interlocked.CompareExchange for atomic check-and-set.
        // Only one thread will successfully change _disposed from 0 to 1.
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return; // Already disposed by another thread
        }
    }

    private static void CleanupFormatting(ReadOnlySpan<char> unformattedString, ref Span<char> buffer, out int bufferIndex)
    {
        bufferIndex = 0;

        for (int i = 0; i < unformattedString.Length; i++)
        {
            switch (unformattedString[i])
            {
                case '%' when i + 1 < unformattedString.Length:
                        switch (unformattedString[i + 1])
                        {
                            case 'n':
                                if (i + 2 >= unformattedString.Length || unformattedString[i + 2] != '\r')
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
    }

    private static bool DoesTemplateMatchPropertyCount(ReadOnlySpan<char> template, int eventPropertyCount)
    {
        if (template.IsEmpty) { return false; }

        string[] dataNodes = GetOrParseTemplateDataNodes(template);

        return dataNodes.Length == eventPropertyCount;
    }

    private static List<string> GetFormattedProperties(ReadOnlySpan<char> template, IEnumerable<object> properties)
    {
        string[]? dataNodes = null;
        List<string> providers = [];

        if (!template.IsEmpty)
        {
            dataNodes = GetOrParseTemplateDataNodes(template);
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

    private static string[] GetOrParseTemplateDataNodes(ReadOnlySpan<char> template)
    {
        var cache = s_formattedPropertiesCache.GetAlternateLookup<ReadOnlySpan<char>>();

        if (cache.TryGetValue(template, out string[]? dataNodes))
        {
            return dataNodes;
        }

        List<string> temp = [];
        ReadOnlySpan<char> dataTag = "<data";
        ReadOnlySpan<char> outTypeAttribute = "outType=\"";

        int searchStart = 0;

        while (searchStart < template.Length)
        {
            int dataIndex = template[searchStart..].IndexOf(dataTag, StringComparison.OrdinalIgnoreCase);

            if (dataIndex == -1) { break; }

            dataIndex += searchStart;

            // Verify the character after "<data" is whitespace, '/', or '>'
            // to avoid matching tags like "<dataSource"
            int nextCharIndex = dataIndex + dataTag.Length;

            if (nextCharIndex < template.Length)
            {
                char next = template[nextCharIndex];

                if (next != ' ' && next != '\t' && next != '\r' && next != '\n' && next != '/' && next != '>')
                {
                    searchStart = nextCharIndex;

                    continue;
                }
            }

            // Find the end of this element
            int elementEnd = template[dataIndex..].IndexOf("/>");

            if (elementEnd == -1)
            {
                elementEnd = template[dataIndex..].IndexOf('>');
            }

            if (elementEnd == -1) { break; }

            elementEnd += dataIndex;

            ReadOnlySpan<char> element = template[dataIndex..elementEnd];
            int outTypeIndex = element.IndexOf(outTypeAttribute, StringComparison.Ordinal);

            if (outTypeIndex != -1)
            {
                outTypeIndex += outTypeAttribute.Length;
                int endIndex = element[outTypeIndex..].IndexOf('"');

                temp.Add(endIndex != -1 ?
                    new string(element.Slice(outTypeIndex, endIndex)) :
                    string.Empty);
            }
            else
            {
                temp.Add(string.Empty);
            }

            searchStart = elementEnd + 1;
        }

        dataNodes = [.. temp];
        cache.TryAdd(template, dataNodes);

        return dataNodes;
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
        IEnumerable<MessageModel> parameters)
    {
        string returnDescription;

        if (string.IsNullOrWhiteSpace(descriptionTemplate))
        {
            // If there is only one property then this is what certain EventRecords look like
            // when the entire description is a string literal, and there is no provider DLL needed.
            // Found a few providers that have their properties wrapped with \r\n for some reason
            returnDescription = properties.Count == 1 ?
                properties[0].TrimEnd('\0', '\r', '\n') :
                DefaultFailedDescription;

            return _cache?.GetOrAddDescription(returnDescription) ?? returnDescription;
        }

        Span<char> description = stackalloc char[descriptionTemplate.Length * 2];

        CleanupFormatting(descriptionTemplate, ref description, out int length);

        description = description[..length];

        if (properties.Count <= 0)
        {
            returnDescription = description.ToString();

            return _cache?.GetOrAddDescription(returnDescription) ?? returnDescription;
        }

        char[] buffer = ArrayPool<char>.Shared.Rent(description.Length * 2);

        try
        {
            int currentLength = 0;
            int lastIndex = 0;
            Span<char> updatedDescription = buffer;

            foreach (var match in _sectionsToReplace.EnumerateMatches(description))
            {
                var sectionToAdd = description[lastIndex..match.Index];

                if (currentLength + sectionToAdd.Length > updatedDescription.Length)
                {
                    ResizeBuffer(ref buffer, ref updatedDescription, sectionToAdd.Length);
                }

                sectionToAdd.CopyTo(updatedDescription[currentLength..]);
                currentLength += sectionToAdd.Length;

                ReadOnlySpan<char> propString = description[match.Index..(match.Index + match.Length)];

                if (!propString.StartsWith("%%") && int.TryParse(propString.Trim(['{', '}', '%']), out var propIndex))
                {
                    // %0 is a Windows Event Log message terminator - skip it entirely
                    if (propIndex == 0)
                    {
                        lastIndex = match.Index + match.Length;

                        continue;
                    }

                    if (propIndex > properties.Count)
                    {
                        Logger?.Warn($"{nameof(FormatDescription)}: Property index out of range - RequestedIndex={propIndex}, PropertyCount={properties.Count}, Template={descriptionTemplate}");

                        return DefaultFailedDescription;
                    }

                    propString = properties[propIndex - 1];
                }

                if (propString.StartsWith("%%") && parameters.Any())
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

            return _cache?.GetOrAddDescription(returnDescription) ?? returnDescription;
        }
        catch (InvalidOperationException ex)
        {
            Logger?.Warn($"{nameof(FormatDescription)}: InvalidOperationException - PropertyCount={properties.Count}, Template={descriptionTemplate}, Exception={ex.Message}");

            // If the regex fails to match, then we just return the original description.
            returnDescription = description.ToString();

            return _cache?.GetOrAddDescription(returnDescription) ?? returnDescription;
        }
        catch (Exception ex)
        {
            Logger?.Warn($"{nameof(FormatDescription)}: Unexpected exception - PropertyCount={properties.Count}, Template={descriptionTemplate}, Exception={ex}");

            return DefaultFailedDescription;
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
            if ((eventRecord.Keywords.Value & k) != k) { continue; }

            var keyword = s_standardKeywords[k].TrimEnd('\0');
            returnValue.Add(_cache?.GetOrAddValue(keyword) ?? keyword);
        }

        if (!ProviderDetails.TryGetValue(eventRecord.ProviderName, out var details) || details is null)
        {
            return returnValue;
        }

        // Some providers re-define the standard keywords in their own metadata,
        // so let's skip those.
        var lower32 = eventRecord.Keywords.Value & 0xFFFFFFFF;

        if (lower32 == 0) { return returnValue; }
        
        foreach (var k in details.Keywords.Keys)
        {
            if ((lower32 & k) != k) { continue; }

            var keyword = details.Keywords[k].TrimEnd('\0');
            returnValue.Add(_cache?.GetOrAddValue(keyword) ?? keyword);
        }
        
        return returnValue;
    }

    private EventModel? GetModernEvent(EventRecord eventRecord, ProviderDetails details)
    {
        if (eventRecord is { Version: null, LogName: null })
        {
            Logger?.Debug($"{nameof(GetModernEvent)}: Skipping modern event lookup - EventId={eventRecord.Id}, Provider={eventRecord.ProviderName} has null Version and LogName");

            return null;
        }

        int eventPropertyCount = eventRecord.Properties.Count;

        // Use indexed lookup instead of linear scan
        var candidateEvents = details.GetEventsById(eventRecord.Id);

        EventModel? modernEvent = null;

        foreach (var e in candidateEvents)
        {
            if (e.Id != eventRecord.Id || e.Version != eventRecord.Version || e.LogName != eventRecord.LogName)
            {
                continue;
            }

            modernEvent = e;

            break;
        }

        if (modernEvent is not null && DoesTemplateMatchPropertyCount(modernEvent.Template, eventPropertyCount))
        {
            Logger?.Debug($"{nameof(GetModernEvent)}: Exact match found - EventId={eventRecord.Id}, Version={eventRecord.Version}, LogName={eventRecord.LogName}");

            return modernEvent;
        }

        if (modernEvent is not null)
        {
            Logger?.Debug($"{nameof(GetModernEvent)}: Exact match found but template property count mismatch - EventId={eventRecord.Id}, Version={eventRecord.Version}, LogName={eventRecord.LogName}, PropertyCount={eventPropertyCount}");
        }

        foreach (var @event in candidateEvents)
        {
            if (@event.LogName != eventRecord.LogName) { continue; }

            if (!DoesTemplateMatchPropertyCount(@event.Template, eventPropertyCount)) { continue; }

            Logger?.Debug($"{nameof(GetModernEvent)}: Match by Id/LogName with template - EventId={eventRecord.Id}, LogName={eventRecord.LogName}, MatchedVersion={@event.Version}");

            return @event;
        }

        // Try again forcing the long to a short and with no log name.
        // This is needed for providers such as Microsoft-Windows-Complus
        foreach (var @event in details.Events)
        {
            if ((short)@event.Id != eventRecord.Id || @event.Version != eventRecord.Version) { continue; }

            if (!DoesTemplateMatchPropertyCount(@event.Template, eventPropertyCount)) { continue; }

            Logger?.Debug($"{nameof(GetModernEvent)}: Match by short Id/Version fallback - EventId={eventRecord.Id}, Version={eventRecord.Version}");

            return @event;
        }

        Logger?.Debug($"{nameof(GetModernEvent)}: No matching event found - EventId={eventRecord.Id}, Version={eventRecord.Version}, LogName={eventRecord.LogName}, PropertyCount={eventPropertyCount}, CandidateEventsWithSameId={candidateEvents.Count}");

        return null;
    }

    /// <summary>Resolve event descriptions from an event record.</summary>
    private string ResolveDescription(EventRecord eventRecord, ProviderDetails? details, EventModel? modernEvent)
    {
        if (details is null)
        {
            Logger?.Debug($"{nameof(ResolveDescription)}: No provider details available - Provider={eventRecord.ProviderName}, EventId={eventRecord.Id}, RecordId={eventRecord.RecordId}");

            return DefaultNoProviderDescription;
        }

        var properties = GetFormattedProperties(modernEvent?.Template, eventRecord.Properties);

        if (!string.IsNullOrEmpty(modernEvent?.Description))
        {
            Logger?.Debug($"{nameof(ResolveDescription)}: Using modern event description - Provider={eventRecord.ProviderName}, EventId={eventRecord.Id}, PropertyCount={properties.Count}");

            return FormatDescription(properties, modernEvent?.Description, details.Parameters);
        }

        // Legacy provider message lookup
        var legacyMessages = details.GetMessagesByShortId(eventRecord.Id);

        if (legacyMessages.Count == 1)
        {
            Logger?.Debug($"{nameof(ResolveDescription)}: Using legacy message - Provider={eventRecord.ProviderName}, EventId={eventRecord.Id}, PropertyCount={properties.Count}");

            return FormatDescription(properties, legacyMessages[0].Text, details.Parameters);
        }

        if (legacyMessages.Count > 1)
        {
            Logger?.Debug($"{nameof(ResolveDescription)}: Multiple legacy messages found, skipping - Provider={eventRecord.ProviderName}, EventId={eventRecord.Id}, MessageCount={legacyMessages.Count}");
        }

        // Some events store the description in the event properties
        if (properties.Count > 0)
        {
            Logger?.Debug($"{nameof(ResolveDescription)}: Using property-based description fallback - Provider={eventRecord.ProviderName}, EventId={eventRecord.Id}, PropertyCount={properties.Count}");

            return FormatDescription(properties, null, details.Parameters);
        }

        Logger?.Debug($"{nameof(ResolveDescription)}: No matching description found - Provider={eventRecord.ProviderName}, EventId={eventRecord.Id}, Version={eventRecord.Version}, LogName={eventRecord.LogName}, RecordId={eventRecord.RecordId}");

        return DefaultNoMatchingDescription;
    }

    /// <summary>Resolve event task names from an event record.</summary>
    private string ResolveTaskName(EventRecord eventRecord, ProviderDetails? details, EventModel? modernEvent)
    {
        if (details is null)
        {
            return string.Empty;
        }

        if (modernEvent?.Task is not null && details.Tasks.TryGetValue(modernEvent.Task, out var taskName))
        {
            taskName = taskName.TrimEnd('\0');
            return _cache?.GetOrAddValue(taskName) ?? taskName;
        }

        if (!eventRecord.Task.HasValue)
        {
            return string.Empty;
        }

        details.Tasks.TryGetValue(eventRecord.Task.Value, out taskName);

        if (taskName is not null)
        {
            taskName = taskName.TrimEnd('\0');
            return _cache?.GetOrAddValue(taskName) ?? taskName;
        }

        var messagesByShortId = details.GetMessagesByShortId(eventRecord.Task.Value);

        List<MessageModel>? potentialTaskNames = null;

        foreach (var m in messagesByShortId)
        {
            if (m.LogLink is null || m.LogLink != eventRecord.LogName) { continue; }

            potentialTaskNames ??= [];
            potentialTaskNames.Add(m);
        }

        if (potentialTaskNames is { Count: > 0 })
        {
            taskName = potentialTaskNames[0].Text;

            if (potentialTaskNames.Count > 1)
            {
                Logger?.Debug($"More than one matching task ID was found.");
                Logger?.Debug($"  eventRecord.Task: {eventRecord.Task}");
                Logger?.Debug($"   Potential matches:");

                potentialTaskNames.ForEach(t => Logger?.Debug($"    {t.LogLink} {t.Text}"));
            }
        }
        else
        {
            taskName = (eventRecord.Task == null) | (eventRecord.Task == 0) ? "None" : $"({eventRecord.Task})";
        }

        taskName = taskName.TrimEnd('\0');

        return _cache?.GetOrAddValue(taskName) ?? taskName;
    }
}
