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

    protected readonly ConcurrentDictionary<string, ProviderDetails?> ProviderDetails =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IEventResolverCache? _cache;
    private readonly ConcurrentDictionary<string, string[]> _formattedPropertiesCache = [];
    private readonly ITraceLogger? _logger;
    private readonly Regex _sectionsToReplace = WildcardWithNumberRegex();
    private readonly ConcurrentDictionary<string, string[]> _visibleOutTypesCache = [];
    private readonly ConcurrentDictionary<string, int> _visiblePropertyCountCache = [];

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

        // If the primary provider couldn't match this event, try a supplemental source.
        // This handles cases where MTA/DB has partial coverage for a provider.
        // Only load supplemental when primary has neither modern events nor legacy messages,
        // to avoid unnecessary local provider loading when primary data is sufficient.
        var descriptionDetails = details;
        ProviderDetails? supplemental = null;

        if (modernEvent is not null ||
            details is null ||
            details.GetMessagesByShortId(eventRecord.Id).Count != 0)
        {
            return CreateEventModel(eventRecord, keywords, modernEvent, descriptionDetails, supplemental);
        }

        supplemental = TryGetSupplementalDetails(eventRecord);

        if (supplemental is null)
        {
            return CreateEventModel(eventRecord, keywords, modernEvent, descriptionDetails, supplemental);
        }

        modernEvent = GetModernEvent(eventRecord, supplemental);

        if (modernEvent is not null)
        {
            descriptionDetails = supplemental;
        }
        else if (supplemental.GetMessagesByShortId(eventRecord.Id).Count > 0)
        {
            // Supplemental has legacy messages for this EventId
            descriptionDetails = supplemental;
        }

        return CreateEventModel(eventRecord, keywords, modernEvent, descriptionDetails, supplemental);
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

    /// <summary>
    ///     Override in derived classes to provide a supplemental ProviderDetails
    ///     when the primary source (MTA/DB) has partial coverage for a provider.
    ///     Called only when the primary provider exists but couldn't match the event.
    /// </summary>
    protected virtual ProviderDetails? TryGetSupplementalDetails(EventRecord eventRecord) => null;

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

    private static string? ExtractAttribute(ReadOnlySpan<char> element, ReadOnlySpan<char> attributePrefix)
    {
        int index = element.IndexOf(attributePrefix, StringComparison.Ordinal);

        if (index == -1) { return null; }

        index += attributePrefix.Length;
        int endIndex = element[index..].IndexOf('"');

        return endIndex != -1 ? new string(element.Slice(index, endIndex)) : null;
    }

    /// <summary>Treats null and empty string as equivalent for LogName comparison.</summary>
    private static bool LogNamesMatch(string? a, string? b) =>
        string.IsNullOrEmpty(a) ? string.IsNullOrEmpty(b) : string.Equals(a, b, StringComparison.Ordinal);

    private static void ResizeBuffer(ref char[] buffer, ref Span<char> source, int sizeToAdd)
    {
        char[] newBuffer = ArrayPool<char>.Shared.Rent(source.Length + sizeToAdd);
        source.CopyTo(newBuffer);
        ArrayPool<char>.Shared.Return(buffer);
        source = buffer = newBuffer;
    }

    [GeneratedRegex("%+[0-9]+")]
    private static partial Regex WildcardWithNumberRegex();

    /// <summary>
    ///     Counts the number of "visible" template properties by excluding length-prefixed
    ///     binary data length fields. When a &lt;data&gt; element has a <c>length</c> attribute
    ///     referencing another &lt;data&gt; element's <c>name</c>, Windows consumes the referenced
    ///     length field internally and does not surface it as a user property via EvtRender.
    /// </summary>
    private int CountVisibleTemplateProperties(ReadOnlySpan<char> template)
    {
        var cache = _visiblePropertyCountCache.GetAlternateLookup<ReadOnlySpan<char>>();

        if (cache.TryGetValue(template, out int cachedCount)) { return cachedCount; }

        List<string> names = [];
        HashSet<string> lengthProviderNames = new(StringComparer.OrdinalIgnoreCase);

        ReadOnlySpan<char> dataTag = "<data";
        ReadOnlySpan<char> nameAttr = "name=\"";
        ReadOnlySpan<char> lengthAttr = "length=\"";

        int searchStart = 0;

        while (searchStart < template.Length)
        {
            int dataIndex = template[searchStart..].IndexOf(dataTag, StringComparison.OrdinalIgnoreCase);

            if (dataIndex == -1) { break; }

            dataIndex += searchStart;

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

            int elementEnd = template[dataIndex..].IndexOf("/>");

            if (elementEnd == -1)
            {
                elementEnd = template[dataIndex..].IndexOf('>');
            }

            if (elementEnd == -1) { break; }

            elementEnd += dataIndex;

            ReadOnlySpan<char> element = template[dataIndex..elementEnd];

            names.Add(ExtractAttribute(element, nameAttr) ?? string.Empty);

            // If this element has a length attribute, the referenced element is a length
            // provider that Windows consumes internally.
            string? lengthRef = ExtractAttribute(element, lengthAttr);

            if (lengthRef is not null)
            {
                lengthProviderNames.Add(lengthRef);
            }

            searchStart = elementEnd + 1;
        }

        if (lengthProviderNames.Count == 0)
        {
            cache.TryAdd(template, names.Count);

            return names.Count;
        }

        // Exclude only the length provider elements; the binary data elements
        // themselves are still surfaced as properties by Windows.
        int visibleCount = 0;

        for (int i = 0; i < names.Count; i++)
        {
            if (string.IsNullOrEmpty(names[i]) || !lengthProviderNames.Contains(names[i]))
            {
                visibleCount++;
            }
        }

        cache.TryAdd(template, visibleCount);

        return visibleCount;
    }

    private DisplayEventModel CreateEventModel(
        EventRecord eventRecord,
        List<string> keywords,
        EventModel? modernEvent,
        ProviderDetails? descriptionDetails,
        ProviderDetails? supplemental) =>
        new(eventRecord.PathName, eventRecord.PathType)
        {
            ActivityId = eventRecord.ActivityId,
            ComputerName = _cache?.GetOrAddValue(eventRecord.ComputerName) ?? eventRecord.ComputerName,
            Description = ResolveDescription(eventRecord, descriptionDetails, modernEvent, supplemental),
            Id = eventRecord.Id,
            Keywords = keywords,
            Level = Severity.GetString(eventRecord.Level),
            LogName = _cache?.GetOrAddValue(eventRecord.LogName) ?? eventRecord.LogName,
            ProcessId = eventRecord.ProcessId,
            RecordId = eventRecord.RecordId,
            Source = _cache?.GetOrAddValue(eventRecord.ProviderName) ?? eventRecord.ProviderName,
            TaskCategory = ResolveTaskName(eventRecord, descriptionDetails, modernEvent),
            ThreadId = eventRecord.ThreadId,
            TimeCreated = eventRecord.TimeCreated,
            UserId = eventRecord.UserId,
            Xml = eventRecord.Xml ?? string.Empty
        };

    /// <summary>
    ///     Relaxed template match for exact Id+Version+LogName matches only.
    ///     Allows the template to have exactly 1 more data node than the event
    ///     has properties, which handles version mismatches where the manifest
    ///     added an optional field in a newer version.
    /// </summary>
    private bool DoesTemplateApproximatelyMatchPropertyCount(ReadOnlySpan<char> template, int eventPropertyCount)
    {
        if (template.IsEmpty || eventPropertyCount <= 0) { return false; }

        int templateCount = CountVisibleTemplateProperties(template);

        if (templateCount == 0)
        {
            templateCount = GetOrParseTemplateDataNodes(template).Length;
        }

        int diff = templateCount - eventPropertyCount;

        // Template may have exactly 1 more field than the event
        return diff == 1;
    }

    private bool DoesTemplateMatchPropertyCount(ReadOnlySpan<char> template, int eventPropertyCount)
    {
        if (template.IsEmpty) { return false; }

        string[] dataNodes = GetOrParseTemplateDataNodes(template);

        if (dataNodes.Length == eventPropertyCount) { return true; }

        // Account for length-prefixed binary data pairs that Windows does not
        // surface as separate user properties via EvtRender.
        return CountVisibleTemplateProperties(template) == eventPropertyCount;
    }

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

        // Guard against stack overflow from very large templates
        const int maxStackAllocChars = 4096;
        int cleanupBufferSize = descriptionTemplate.Length * 2;
        char[]? cleanupRented = null;

        Span<char> description = cleanupBufferSize <= maxStackAllocChars
            ? stackalloc char[cleanupBufferSize]
            : (cleanupRented = ArrayPool<char>.Shared.Rent(cleanupBufferSize));

        CleanupFormatting(descriptionTemplate, ref description, out int length);

        description = description[..length];

        if (properties.Count <= 0 && description.IndexOf("%%".AsSpan()) < 0)
        {
            returnDescription = description.ToString();

            if (cleanupRented is not null) { ArrayPool<char>.Shared.Return(cleanupRented); }

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
                        Logger?.Debug($"{nameof(FormatDescription)}: Property index out of range - RequestedIndex={propIndex}, PropertyCount={properties.Count}, Template={descriptionTemplate}");

                        // Substitute with empty string rather than failing the entire description.
                        // This commonly occurs when a manifest template references more properties
                        // than the event actually supplies (e.g., version mismatch or optional data).
                        // The available properties are still correctly positional.
                        propString = ReadOnlySpan<char>.Empty;
                    }
                    else
                    {
                        propString = properties[propIndex - 1];
                    }
                }

                if (propString.StartsWith("%%"))
                {
                    int endParameterId = propString.IndexOf(' ');

                    var parameterIdString = endParameterId > 2
                        ? propString[2..endParameterId]
                        : propString[2..];

                    if (long.TryParse(parameterIdString, out long parameterId))
                    {
                        // Some parameters exceed int size and need to be cast from long to int
                        // because they are actually negative numbers
                        ReadOnlySpan<char> parameterMessage =
                            parameters.FirstOrDefault(m => m.RawId == (int)parameterId)?.Text ?? string.Empty;

                        // Fallback to system FormatMessage for Win32 error codes
                        // when provider parameters aren't available (e.g., MTA/DB resolvers)
                        if (parameterMessage.IsEmpty && parameterId is > 0 and <= uint.MaxValue)
                        {
                            parameterMessage = NativeMethods.FormatSystemMessage((uint)parameterId);
                        }

                        if (!parameterMessage.IsEmpty)
                        {
                            // Remove only an exact trailing "%0" terminator, not individual chars
                            parameterMessage = parameterMessage.EndsWith("%0")
                                ? parameterMessage[..^2]
                                : parameterMessage;

                            propString = endParameterId > 2 ?
                                string.Concat(parameterMessage, propString[(endParameterId + 1)..]) :
                                parameterMessage;
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

            if (cleanupRented is not null) { ArrayPool<char>.Shared.Return(cleanupRented); }
        }
    }

    private List<string> GetFormattedProperties(ReadOnlySpan<char> template, IReadOnlyList<object> properties)
    {
        string[]? dataNodes = null;
        List<string> formattedValues = [];

        if (!template.IsEmpty)
        {
            // EvtRender may or may not include hidden length-provider fields in its output.
            // Choose the outType array whose length matches the actual property count.
            // If neither matches, skip outType formatting to avoid misalignment.
            var visibleOutTypes = GetVisibleTemplateOutTypes(template);

            if (visibleOutTypes.Length == properties.Count)
            {
                dataNodes = visibleOutTypes;
            }
            else
            {
                var allOutTypes = GetOrParseTemplateDataNodes(template);

                if (allOutTypes.Length == properties.Count)
                {
                    dataNodes = allOutTypes;
                }
            }
        }

        int index = 0;

        foreach (object property in properties)
        {
            string? outType = index < dataNodes?.Length ? dataNodes[index] : null;

            switch (property)
            {
                case bool boolValue:
                    formattedValues.Add(boolValue ? "true" : "false");

                    break;
                case DateTime eventTime:
                    formattedValues.Add(eventTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffff00K"));

                    break;
                case byte[] bytes:
                    formattedValues.Add(Convert.ToHexString(bytes));

                    break;
                case SecurityIdentifier sid:
                    formattedValues.Add(sid.Value);

                    break;
                default:
                    if (string.IsNullOrEmpty(outType))
                    {
                        formattedValues.Add($"{property}");
                    }
                    else if (s_displayAsHexTypes.Contains(outType, StringComparer.OrdinalIgnoreCase))
                    {
                        formattedValues.Add(property switch
                        {
                            byte b => $"0x{b:X}",
                            sbyte sb => $"0x{sb:X}",
                            short s => $"0x{s:X}",
                            ushort us => $"0x{us:X}",
                            int i => $"0x{i:X}",
                            uint ui => $"0x{ui:X}",
                            long l => $"0x{l:X}",
                            ulong ul => $"0x{ul:X}",
                            _ => $"{property}"
                        });
                    }
                    else if (string.Equals(outType, "win:HResult", StringComparison.OrdinalIgnoreCase) && property is int hResult)
                    {
                        formattedValues.Add(ResolverMethods.GetErrorMessage((uint)hResult));
                    }
                    else if (string.Equals(outType, "win:NTStatus", StringComparison.OrdinalIgnoreCase))
                    {
                        uint statusCode = property switch
                        {
                            uint ui => ui,
                            int i => (uint)i,
                            ulong ul => (uint)ul,
                            long l => (uint)l,
                            ushort us => us,
                            short s => (uint)s,
                            byte b => b,
                            _ => 0
                        };

                        formattedValues.Add(property is uint or int or ulong or long or ushort or short or byte
                            ? ResolverMethods.GetNtStatusMessage(statusCode)
                            : $"{property}");
                    }
                    else
                    {
                        formattedValues.Add($"{property}");
                    }

                    break;
            }

            index++;
        }

        return formattedValues;
    }

    private List<string> GetKeywordsFromBitmask(EventRecord eventRecord)
    {
        if (eventRecord.Keywords is null or 0) { return []; }

        var keywordsValue = eventRecord.Keywords.Value;
        List<string> returnValue = [];

        // Standard (Microsoft-defined) keywords live in bits 48–55. Skip the entire
        // standard-keyword scan when the event has no bits in that range.
        if ((keywordsValue & 0x00FF_0000_0000_0000L) != 0)
        {
            foreach (var (bit, name) in s_standardKeywords)
            {
                if ((keywordsValue & bit) != bit) { continue; }

                var keyword = name.TrimEnd('\0');
                returnValue.Add(_cache?.GetOrAddValue(keyword) ?? keyword);
            }
        }

        if (!ProviderDetails.TryGetValue(eventRecord.ProviderName, out var details) || details is null)
        {
            return returnValue;
        }

        // Provider-defined keywords use bits 0–47; bits 48–63 are reserved
        // for Microsoft-defined standard keywords handled above.
        var providerBits = keywordsValue & 0x0000_FFFF_FFFF_FFFFL;

        if (providerBits == 0) { return returnValue; }

        foreach (var (bit, name) in details.Keywords)
        {
            if ((providerBits & bit) != bit) { continue; }

            var keyword = name.TrimEnd('\0');
            returnValue.Add(_cache?.GetOrAddValue(keyword) ?? keyword);
        }

        return returnValue;
    }

    private EventModel? GetModernEvent(EventRecord eventRecord, ProviderDetails details)
    {
        if (eventRecord.Version is null && string.IsNullOrEmpty(eventRecord.LogName))
        {
            Logger?.Debug($"{nameof(GetModernEvent)}: Skipping modern event lookup - EventId={eventRecord.Id}, Provider={eventRecord.ProviderName} has null Version and empty LogName");

            return null;
        }

        int eventPropertyCount = eventRecord.Properties.Count;

        // Use indexed lookup instead of linear scan
        var candidateEvents = details.GetEventsById(eventRecord.Id);

        EventModel? modernEvent = null;

        foreach (var e in candidateEvents)
        {
            if (e.Id != eventRecord.Id ||
                e.Version != eventRecord.Version ||
                !LogNamesMatch(e.LogName, eventRecord.LogName))
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

        // For exact Id+Version+LogName matches, tolerate the template having slightly
        // more data nodes than the event supplies. This handles version mismatches where
        // the manifest added optional fields. FormatDescription handles out-of-range
        // property indices gracefully by substituting empty string.
        if (modernEvent is not null && DoesTemplateApproximatelyMatchPropertyCount(modernEvent.Template, eventPropertyCount))
        {
            Logger?.Debug($"{nameof(GetModernEvent)}: Exact match with relaxed template count - EventId={eventRecord.Id}, Version={eventRecord.Version}, LogName={eventRecord.LogName}, PropertyCount={eventPropertyCount}");

            return modernEvent;
        }

        if (modernEvent is not null)
        {
            Logger?.Debug($"{nameof(GetModernEvent)}: Exact match found but template property count mismatch - EventId={eventRecord.Id}, Version={eventRecord.Version}, LogName={eventRecord.LogName}, PropertyCount={eventPropertyCount}");
        }

        foreach (var @event in candidateEvents)
        {
            if (!LogNamesMatch(@event.LogName, eventRecord.LogName)) { continue; }

            if (!DoesTemplateMatchPropertyCount(@event.Template, eventPropertyCount)) { continue; }

            Logger?.Debug($"{nameof(GetModernEvent)}: Match by Id/LogName with template - EventId={eventRecord.Id}, LogName={eventRecord.LogName}, MatchedVersion={@event.Version}");

            return @event;
        }

        // Try again forcing the long to a short and with no log name.
        // This is needed for providers such as Microsoft-Windows-Complus
        EventModel? shortIdMatch = null;

        foreach (var @event in details.Events)
        {
            if ((short)@event.Id != eventRecord.Id || @event.Version != eventRecord.Version) { continue; }

            if (!DoesTemplateMatchPropertyCount(@event.Template, eventPropertyCount)) { continue; }

            if (shortIdMatch is not null)
            {
                // Multiple matches — ambiguous
                shortIdMatch = null;

                break;
            }

            shortIdMatch = @event;
        }

        if (shortIdMatch is not null)
        {
            Logger?.Debug($"{nameof(GetModernEvent)}: Match by short Id/Version fallback - EventId={eventRecord.Id}, Version={eventRecord.Version}");

            return shortIdMatch;
        }

        // Final fallback: match by Id+Version ignoring LogName, but only if exactly
        // one candidate passes template validation. This handles providers that define
        // events under diagnostic/operational channels but log to Application/System
        // via eventlog redirects (e.g., Winlogon 6001).
        EventModel? logNameIgnoredMatch = null;

        foreach (var @event in candidateEvents)
        {
            if (@event.Version != eventRecord.Version) { continue; }

            if (!DoesTemplateMatchPropertyCount(@event.Template, eventPropertyCount)) { continue; }

            if (logNameIgnoredMatch is not null)
            {
                // Multiple candidates — ambiguous, don't guess
                logNameIgnoredMatch = null;

                break;
            }

            logNameIgnoredMatch = @event;
        }

        if (logNameIgnoredMatch is not null)
        {
            Logger?.Debug($"{nameof(GetModernEvent)}: Unique match by Id/Version ignoring LogName - EventId={eventRecord.Id}, Version={eventRecord.Version}, MatchedLogName={logNameIgnoredMatch.LogName}");

            return logNameIgnoredMatch;
        }

        Logger?.Debug($"{nameof(GetModernEvent)}: No matching event found - EventId={eventRecord.Id}, Version={eventRecord.Version}, LogName={eventRecord.LogName}, PropertyCount={eventPropertyCount}, CandidateEventsWithSameId={candidateEvents.Count}");

        return null;
    }

    private string[] GetOrParseTemplateDataNodes(ReadOnlySpan<char> template)
    {
        var cache = _formattedPropertiesCache.GetAlternateLookup<ReadOnlySpan<char>>();

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

    /// <summary>
    ///     Returns the best available parameter collection for FormatDescription.
    ///     Only loads supplemental parameters when the primary provider lacks them,
    ///     which occurs for MTA/DB providers that don't load parameter DLLs.
    /// </summary>
    private IEnumerable<MessageModel> GetParametersWithFallback(
        ProviderDetails details,
        ref ProviderDetails? supplemental,
        EventRecord eventRecord)
    {
        if (details.Parameters.Any()) { return details.Parameters; }

        supplemental ??= TryGetSupplementalDetails(eventRecord);

        return supplemental?.Parameters ?? details.Parameters;
    }

    /// <summary>
    ///     Returns the outType values for only visible template properties, excluding hidden
    ///     length-provider fields that Windows consumes internally. This ensures the outType
    ///     array aligns correctly with the properties surfaced by EvtRender.
    /// </summary>
    private string[] GetVisibleTemplateOutTypes(ReadOnlySpan<char> template)
    {
        var cache = _visibleOutTypesCache.GetAlternateLookup<ReadOnlySpan<char>>();

        if (cache.TryGetValue(template, out string[]? cached)) { return cached; }

        List<(string name, string outType)> elements = [];
        HashSet<string> lengthProviderNames = new(StringComparer.OrdinalIgnoreCase);

        ReadOnlySpan<char> dataTag = "<data";
        ReadOnlySpan<char> nameAttr = "name=\"";
        ReadOnlySpan<char> outTypeAttr = "outType=\"";
        ReadOnlySpan<char> lengthAttr = "length=\"";

        int searchStart = 0;

        while (searchStart < template.Length)
        {
            int dataIndex = template[searchStart..].IndexOf(dataTag, StringComparison.OrdinalIgnoreCase);

            if (dataIndex == -1) { break; }

            dataIndex += searchStart;

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

            int elementEnd = template[dataIndex..].IndexOf("/>");

            if (elementEnd == -1)
            {
                elementEnd = template[dataIndex..].IndexOf('>');
            }

            if (elementEnd == -1) { break; }

            elementEnd += dataIndex;

            ReadOnlySpan<char> element = template[dataIndex..elementEnd];

            string name = ExtractAttribute(element, nameAttr) ?? string.Empty;
            string outType = ExtractAttribute(element, outTypeAttr) ?? string.Empty;

            elements.Add((name, outType));

            string? lengthRef = ExtractAttribute(element, lengthAttr);

            if (lengthRef is not null)
            {
                lengthProviderNames.Add(lengthRef);
            }

            searchStart = elementEnd + 1;
        }

        string[] result;

        if (lengthProviderNames.Count == 0)
        {
            result = elements.Select(e => e.outType).ToArray();
        }
        else
        {
            result = elements
                .Where(e => string.IsNullOrEmpty(e.name) || !lengthProviderNames.Contains(e.name))
                .Select(e => e.outType)
                .ToArray();
        }

        cache.TryAdd(template, result);

        return result;
    }

    /// <summary>Resolve event descriptions from an event record.</summary>
    private string ResolveDescription(
        EventRecord eventRecord,
        ProviderDetails? details,
        EventModel? modernEvent,
        ProviderDetails? supplemental)
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

            return FormatDescription(properties, modernEvent!.Description,
                GetParametersWithFallback(details, ref supplemental, eventRecord));
        }

        // Legacy provider message lookup
        var legacyMessages = details.GetMessagesByShortId(eventRecord.Id);

        if (legacyMessages.Count == 1)
        {
            Logger?.Debug($"{nameof(ResolveDescription)}: Using legacy message - Provider={eventRecord.ProviderName}, EventId={eventRecord.Id}, PropertyCount={properties.Count}");

            return FormatDescription(properties, legacyMessages[0].Text,
                GetParametersWithFallback(details, ref supplemental, eventRecord));
        }

        if (legacyMessages.Count > 1)
        {
            // Disambiguate by LogLink, matching the pattern used in ResolveTaskName
            MessageModel? bestMatch = null;

            foreach (var m in legacyMessages)
            {
                if (m.LogLink is not null && LogNamesMatch(m.LogLink, eventRecord.LogName))
                {
                    bestMatch = m;

                    break;
                }
            }

            // If LogLink didn't disambiguate, try severity-based matching.
            // MC RawId bits 31-30 encode severity: 00=Success, 01=Informational, 10=Warning, 11=Error
            // ETW levels: 0=LogAlways, 1=Critical, 2=Error, 3=Warning, 4=Information, 5=Verbose
            if (bestMatch is null && eventRecord.Level is not null)
            {
                int targetSeverity = eventRecord.Level switch
                {
                    1 or 2 => 3,    // Critical/Error → severity 11 (Error)
                    3 => 2,         // Warning → severity 10 (Warning)
                    4 or 5 => 1,    // Information/Verbose → severity 01 (Informational)
                    _ => 0          // LogAlways → severity 00 (Success)
                };

                MessageModel? severityCandidate = null;

                foreach (var m in legacyMessages)
                {
                    int messageSeverity = (int)((m.RawId >> 30) & 0x3);

                    if (messageSeverity != targetSeverity) { continue; }

                    if (severityCandidate is not null)
                    {
                        // Multiple matches with same severity — still ambiguous
                        severityCandidate = null;

                        break;
                    }

                    severityCandidate = m;
                }

                bestMatch = severityCandidate;

                if (bestMatch is not null)
                {
                    Logger?.Debug($"{nameof(ResolveDescription)}: Disambiguated legacy message by severity - Provider={eventRecord.ProviderName}, EventId={eventRecord.Id}, Level={eventRecord.Level}, Severity={(bestMatch.RawId >> 30) & 0x3}");
                }
            }

            if (bestMatch is not null)
            {
                return FormatDescription(properties, bestMatch.Text,
                    GetParametersWithFallback(details, ref supplemental, eventRecord));
            }

            Logger?.Debug($"{nameof(ResolveDescription)}: Multiple legacy messages found, could not disambiguate - Provider={eventRecord.ProviderName}, EventId={eventRecord.Id}, MessageCount={legacyMessages.Count}");
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
            taskName = eventRecord.Task == 0 ? "None" : $"({eventRecord.Task})";
        }

        taskName = taskName.TrimEnd('\0');

        return _cache?.GetOrAddValue(taskName) ?? taskName;
    }
}
