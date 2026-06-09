// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Interop;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Resolution;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace EventLogExpert.Eventing.Resolvers;

public partial class EventResolverBase : IDisposable
{
    private const string DefaultFailedDescription = "Failed to resolve description, see XML for more details.";
    private const string DefaultNoMatchingDescription = "No matching message found with loaded providers, see XML for more details.";
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

    protected readonly ConcurrentDictionary<string, ProviderDetails?> ProviderDetails =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IEventResolverCache? _cache;
    private readonly ITraceLogger? _logger;
    private readonly Regex _sectionsToReplace = WildcardWithNumberRegex();
    private readonly TaskKeywordResolver _taskKeywords;
    private readonly TemplateAnalyzer _templates = new();

    private int _disposed;

    protected EventResolverBase(IEventResolverCache? cache = null, ITraceLogger? logger = null)
    {
        _cache = cache;
        _logger = logger;
        _taskKeywords = new TaskKeywordResolver(cache, logger);
    }

    protected bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    protected ITraceLogger? Logger => _logger;

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public virtual ResolvedEvent ResolveEvent(EventRecord eventRecord)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        // Resolve the modern event once and reuse for both description and task name
        ProviderDetails.TryGetValue(eventRecord.ProviderName, out var details);
        var modernEvent = details is not null ? GetModernEvent(eventRecord, details) : null;

        var descriptionDetails = details;
        ProviderDetails? supplemental = null;

        // Primary is decisive when modernEvent matched or there's a single unambiguous legacy
        // message. In that case skip supplemental loading entirely.
        if (modernEvent is not null || details is null)
        {
            return CreateEventModel(eventRecord, modernEvent, details, descriptionDetails, supplemental, null);
        }

        var primaryLegacyCount = details.GetMessagesByShortId(eventRecord.Id).Count;

        if (primaryLegacyCount == 1)
        {
            return CreateEventModel(eventRecord, modernEvent, details, descriptionDetails, supplemental, null);
        }

        // Primary is non-decisive: either has no match (count == 0) or has multiple ambiguous
        // legacy messages (count > 1). Load supplemental so it's available to description,
        // task, and keyword resolution consistently. ResolveDescription will use supplemental
        // as a disambiguation fallback in the count > 1 case.
        supplemental = TryGetSupplementalDetails(eventRecord);

        EventModel? supplementalModernEvent = supplemental is not null
            ? GetModernEvent(eventRecord, supplemental)
            : null;

        if (supplemental is not null && primaryLegacyCount == 0)
        {
            // Primary has no match at all — promote supplemental as the description source
            // when it matches. For count > 1, leave primary as the description source so its
            // disambiguation runs first; supplemental becomes a tiebreaker inside ResolveDescription.
            if (supplementalModernEvent is not null)
            {
                modernEvent = supplementalModernEvent;
                descriptionDetails = supplemental;
            }
            else if (supplemental.GetMessagesByShortId(eventRecord.Id).Count > 0)
            {
                // Supplemental has legacy messages for this EventId
                descriptionDetails = supplemental;
            }
        }

        return CreateEventModel(eventRecord, modernEvent, details, descriptionDetails, supplemental, supplementalModernEvent);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing) { return; }

        // Atomically guard against double-disposal: only the first caller observes the prior 0.
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) { }
    }

    /// <summary>
    ///     Override in derived classes to provide a supplemental ProviderDetails when the primary source (MTA/DB) has
    ///     partial coverage for a provider. Called only when the primary provider exists but couldn't match the event.
    /// </summary>
    protected virtual ProviderDetails? TryGetSupplementalDetails(EventRecord eventRecord) => null;

    private static string? BuildEventDataTail(List<string> properties)
    {
        if (properties.Count == 0) { return null; }

        StringBuilder builder = new();
        builder.Append("The following information was included with the event:\r\n");

        foreach (var property in properties)
        {
            builder.Append("\r\n").Append(property);
        }

        return builder.ToString();
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

    /// <summary>
    ///     Disambiguate multiple legacy messages for the same event ID. Tries Qualifier match (high 16 bits of RawId),
    ///     then LogLink, then severity. Returns null when the set is empty or remains ambiguous after all checks.
    /// </summary>
    private static MessageModel? TryDisambiguateLegacyMessage(EventRecord eventRecord, IReadOnlyList<MessageModel> legacyMessages)
    {
        if (legacyMessages.Count == 0) { return null; }

        if (legacyMessages.Count == 1) { return legacyMessages[0]; }

        // Qualifier match. For classic/legacy events, Windows encodes Qualifiers in
        // the high 16 bits of the message ID, so RawId == (Qualifiers << 16) | EventId
        // identifies the exact message-table entry.
        if (eventRecord.Qualifiers.HasValue)
        {
            List<MessageModel>? qualifierMatches = null;

            foreach (var m in legacyMessages)
            {
                if ((ushort)((m.RawId >> 16) & 0xFFFF) == eventRecord.Qualifiers.Value)
                {
                    (qualifierMatches ??= []).Add(m);
                }
            }

            if (qualifierMatches is { Count: 1 })
            {
                return qualifierMatches[0];
            }

            if (qualifierMatches is { Count: > 1 })
            {
                legacyMessages = qualifierMatches;
            }
        }

        // LogLink match
        foreach (var m in legacyMessages)
        {
            if (m.LogLink is not null && LogNamesMatch(m.LogLink, eventRecord.LogName))
            {
                return m;
            }
        }

        // Severity-based match. MC RawId bits 31-30 encode severity:
        // 00=Success, 01=Informational, 10=Warning, 11=Error.
        // ETW levels: 0=LogAlways, 1=Critical, 2=Error, 3=Warning, 4=Information, 5=Verbose.
        if (eventRecord.Level is null) { return null; }

        int targetSeverity = eventRecord.Level switch
        {
            1 or 2 => 3,
            3 => 2,
            4 or 5 => 1,
            _ => 0
        };

        MessageModel? severityCandidate = null;

        foreach (var m in legacyMessages)
        {
            int messageSeverity = (int)((m.RawId >> 30) & 0x3);

            if (messageSeverity != targetSeverity) { continue; }

            if (severityCandidate is not null)
            {
                // Multiple matches with same severity — still ambiguous
                return null;
            }

            severityCandidate = m;
        }

        return severityCandidate;
    }

    [GeneratedRegex("%+[0-9]+")]
    private static partial Regex WildcardWithNumberRegex();

    private string BuildNoMetadataFallbackDescription(EventRecord eventRecord, List<string> properties)
    {
        const long classicKeywordBit = 0x0080000000000000L;
        bool isClassic = ((eventRecord.Keywords ?? 0) & classicKeywordBit) != 0;

        string? systemMessage = null;

        if (isClassic && eventRecord.Id == 0)
        {
            // EventId 0 with the Classic keyword bit is what mmc renders as the Win32
            // ERROR_SUCCESS text ("The operation completed successfully."). The Win32
            // ERROR_SUCCESS code happens to be 0 too, but we are deliberately requesting
            // the ERROR_SUCCESS message — not treating the EventId as a Win32 error code.
            const uint Win32ErrorSuccess = 0;
            systemMessage = NativeMethods.FormatSystemMessage(Win32ErrorSuccess);
        }

        string? propertyTail = BuildEventDataTail(properties);

        // The propertyTail varies per event (timestamps, paths, IDs, etc.) — caching it
        // would grow the description cache unboundedly. Only cache the low-cardinality
        // canned strings (DefaultNoProviderDescription, systemMessage).
        if (propertyTail is not null)
        {
            return string.IsNullOrWhiteSpace(systemMessage)
                ? propertyTail
                : $"{systemMessage}\r\n\r\n{propertyTail}";
        }

        string fallback = string.IsNullOrWhiteSpace(systemMessage)
            ? DefaultNoProviderDescription
            : systemMessage!;

        return _cache?.GetOrAddDescription(fallback) ?? fallback;
    }

    private ResolvedEvent CreateEventModel(
        EventRecord eventRecord,
        EventModel? modernEvent,
        ProviderDetails? details,
        ProviderDetails? descriptionDetails,
        ProviderDetails? supplemental,
        EventModel? supplementalModernEvent) =>
        new(eventRecord.PathName, eventRecord.LogPathType)
        {
            ActivityId = eventRecord.ActivityId,
            ComputerName = _cache?.GetOrAddValue(eventRecord.ComputerName) ?? eventRecord.ComputerName,
            Description = ResolveDescription(eventRecord, details, descriptionDetails, modernEvent, supplemental, supplementalModernEvent),
            Id = eventRecord.Id,
            Keywords = _taskKeywords.GetKeywords(eventRecord, details, supplemental),
            Level = SeverityFormatter.Format(eventRecord.Level),
            LogName = _cache?.GetOrAddValue(eventRecord.LogName) ?? eventRecord.LogName,
            ProcessId = eventRecord.ProcessId,
            RecordId = eventRecord.RecordId,
            Source = _cache?.GetOrAddValue(eventRecord.ProviderName) ?? eventRecord.ProviderName,
            TaskCategory = _taskKeywords.ResolveTaskName(eventRecord, details, modernEvent, supplemental, supplementalModernEvent),
            ThreadId = eventRecord.ThreadId,
            TimeCreated = eventRecord.TimeCreated,
            UserId = eventRecord.UserId,
            Xml = eventRecord.Xml ?? string.Empty
        };

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
            // Found a few providers that have their properties wrapped with \r\n for some reason.
            // Multi-property fall-through is a defensive backstop (e.g. a legacy message row with
            // empty Text); DefaultFailedDescription is reserved for actual formatting exceptions.
            returnDescription = properties.Count == 1 ?
                properties[0].TrimEnd('\0', '\r', '\n') :
                DefaultNoMatchingDescription;

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
            Logger?.Warning($"{nameof(FormatDescription)}: InvalidOperationException - PropertyCount={properties.Count}, Template={descriptionTemplate}, Exception={ex.Message}");

            // If the regex fails to match, then we just return the original description.
            returnDescription = description.ToString();

            return _cache?.GetOrAddDescription(returnDescription) ?? returnDescription;
        }
        catch (Exception ex)
        {
            Logger?.Warning($"{nameof(FormatDescription)}: Unexpected exception - PropertyCount={properties.Count}, Template={descriptionTemplate}, Exception={ex}");

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
        ImmutableArray<string> dataNodes = default;
        List<string> formattedValues = [];

        if (!template.IsEmpty)
        {
            // EvtRender may or may not include hidden length-provider fields in its output.
            // Choose the outType array whose length matches the actual property count.
            // If neither matches, skip outType formatting to avoid misalignment.
            var meta = _templates.Analyze(template);

            if (meta.VisibleOutTypes.Length == properties.Count)
            {
                dataNodes = meta.VisibleOutTypes;
            }
            else if (meta.AllOutTypes.Length == properties.Count)
            {
                dataNodes = meta.AllOutTypes;
            }
        }

        int index = 0;

        foreach (object property in properties)
        {
            string? outType = !dataNodes.IsDefault && index < dataNodes.Length ? dataNodes[index] : null;

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
                        formattedValues.Add(NativeErrorResolver.GetErrorMessage((uint)hResult));
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
                            ? NativeErrorResolver.GetNtStatusMessage(statusCode)
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

        if (modernEvent is not null && _templates.StrictlyMatchesPropertyCount(modernEvent.Template, eventPropertyCount))
        {
            Logger?.Debug($"{nameof(GetModernEvent)}: Exact match found - EventId={eventRecord.Id}, Version={eventRecord.Version}, LogName={eventRecord.LogName}");

            return modernEvent;
        }

        // For exact Id+Version+LogName matches, tolerate the template having slightly
        // more data nodes than the event supplies. This handles version mismatches where
        // the manifest added optional fields. FormatDescription handles out-of-range
        // property indices gracefully by substituting empty string.
        if (modernEvent is not null && _templates.ApproximatelyMatchesPropertyCount(modernEvent.Template, eventPropertyCount))
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

            if (!_templates.MatchesPropertyCount(@event.Template, eventPropertyCount)) { continue; }

            Logger?.Debug($"{nameof(GetModernEvent)}: Match by Id/LogName with template - EventId={eventRecord.Id}, LogName={eventRecord.LogName}, MatchedVersion={@event.Version}");

            return @event;
        }

        // Try again forcing the long to a short and with no log name. Needed for providers
        // such as Microsoft-Windows-Complus and Microsoft-Windows-WPDClassInstaller that store
        // events with the full 32-bit RawId (high 16 bits = Qualifiers, low 16 bits = EventId).
        // Strict template match accepts empty template + zero EventData properties, which is the
        // shape WPDClassInstaller emits for its full-RawId entries. When the record carries a
        // Qualifiers value, prefer the exact full-RawId match before the low-16 ambiguity check.
        if (eventRecord.Qualifiers is { } qualifier)
        {
            var fullRawId = ((long)qualifier << 16) | eventRecord.Id;

            foreach (var @event in details.Events)
            {
                if (@event.Id != fullRawId || @event.Version != eventRecord.Version) { continue; }

                if (!_templates.StrictlyMatchesPropertyCount(@event.Template, eventPropertyCount)) { continue; }

                Logger?.Debug($"{nameof(GetModernEvent)}: Match by full RawId fallback - RawId={fullRawId:X}, Version={eventRecord.Version}");

                return @event;
            }
        }

        EventModel? shortIdMatch = null;

        foreach (var @event in details.Events)
        {
            if ((ushort)@event.Id != eventRecord.Id || @event.Version != eventRecord.Version) { continue; }

            // When the record's qualifier is known, full-RawId entries with conflicting high
            // bits would have been caught above. Skip them here so they do not mask short-only
            // entries or trigger a false ambiguity.
            if (eventRecord.Qualifiers is not null && @event.Id > ushort.MaxValue) { continue; }

            if (!_templates.StrictlyMatchesPropertyCount(@event.Template, eventPropertyCount)) { continue; }

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

            if (!_templates.MatchesPropertyCount(@event.Template, eventPropertyCount)) { continue; }

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

    /// <summary>
    ///     Picks the parameter table for %%n substitutions, biased toward whichever provider supplied the description
    ///     text. When <paramref name="descriptionFromSupplemental" /> is true, prefer supplemental's parameters and fall back
    ///     to primary; otherwise prefer primary and fall back to supplemental (lazily loading it when not yet available).
    /// </summary>
    private IEnumerable<MessageModel> GetParametersForDescription(
        ProviderDetails? primary,
        ProviderDetails? supplementalDetails,
        bool descriptionFromSupplemental,
        ref ProviderDetails? supplemental,
        EventRecord eventRecord)
    {
        if (descriptionFromSupplemental && supplementalDetails is not null)
        {
            if (supplementalDetails.Parameters.Any()) { return supplementalDetails.Parameters; }

            return primary?.Parameters ?? supplementalDetails.Parameters;
        }

        if (primary is not null && primary.Parameters.Any()) { return primary.Parameters; }

        supplemental ??= TryGetSupplementalDetails(eventRecord);

        return supplemental?.Parameters ?? primary?.Parameters ?? [];
    }

    /// <summary>Resolve event descriptions from an event record.</summary>
    private string ResolveDescription(
        EventRecord eventRecord,
        ProviderDetails? primaryDetails,
        ProviderDetails? details,
        EventModel? modernEvent,
        ProviderDetails? supplemental,
        EventModel? supplementalModernEvent)
    {
        if (details is null)
        {
            Logger?.Debug($"{nameof(ResolveDescription)}: No provider details available - Provider={eventRecord.ProviderName}, EventId={eventRecord.Id}, RecordId={eventRecord.RecordId}");

            return DefaultNoProviderDescription;
        }

        var properties = GetFormattedProperties(modernEvent?.Template, eventRecord.Properties);

        var descriptionFromSupplemental = supplemental is not null && ReferenceEquals(details, supplemental);

        if (!string.IsNullOrEmpty(modernEvent?.Description))
        {
            Logger?.Debug($"{nameof(ResolveDescription)}: Using modern event description - Provider={eventRecord.ProviderName}, EventId={eventRecord.Id}, PropertyCount={properties.Count}");

            return FormatDescription(properties, modernEvent!.Description,
                GetParametersForDescription(primaryDetails, supplemental, descriptionFromSupplemental, ref supplemental, eventRecord));
        }

        // Legacy provider message lookup
        var legacyMessages = details.GetMessagesByShortId(eventRecord.Id);

        if (legacyMessages.Count == 1)
        {
            Logger?.Debug($"{nameof(ResolveDescription)}: Using legacy message - Provider={eventRecord.ProviderName}, EventId={eventRecord.Id}, PropertyCount={properties.Count}");

            return FormatDescription(properties, legacyMessages[0].Text,
                GetParametersForDescription(primaryDetails, supplemental, descriptionFromSupplemental, ref supplemental, eventRecord));
        }

        if (legacyMessages.Count > 1)
        {
            var bestMatch = TryDisambiguateLegacyMessage(eventRecord, legacyMessages);

            if (bestMatch is not null)
            {
                Logger?.Debug($"{nameof(ResolveDescription)}: Disambiguated legacy message - Provider={eventRecord.ProviderName}, EventId={eventRecord.Id}, Level={eventRecord.Level}");

                return FormatDescription(properties, bestMatch.Text,
                    GetParametersForDescription(primaryDetails, supplemental, descriptionFromSupplemental, ref supplemental, eventRecord));
            }

            Logger?.Debug($"{nameof(ResolveDescription)}: Multiple legacy messages found, could not disambiguate - Provider={eventRecord.ProviderName}, EventId={eventRecord.Id}, MessageCount={legacyMessages.Count}");

            // Last-resort: ambiguous primary may be resolvable via supplemental. ResolveEvent
            // pre-loads supplemental and its modern event for count > 1, so both are already
            // set here when supplemental is available.
            if (supplemental is not null && !ReferenceEquals(supplemental, details))
            {
                if (!string.IsNullOrEmpty(supplementalModernEvent?.Description))
                {
                    Logger?.Debug($"{nameof(ResolveDescription)}: Disambiguated via supplemental modern event - Provider={eventRecord.ProviderName}, EventId={eventRecord.Id}");

                    var supplementalProperties = GetFormattedProperties(supplementalModernEvent!.Template, eventRecord.Properties);

                    // Description came from supplemental, so resolve %%n parameter substitutions
                    // against supplemental's parameter table first.
                    return FormatDescription(supplementalProperties, supplementalModernEvent.Description,
                        GetParametersForDescription(primaryDetails, supplemental, true, ref supplemental, eventRecord));
                }

                var supplementalLegacy = supplemental.GetMessagesByShortId(eventRecord.Id);
                var supplementalBest = TryDisambiguateLegacyMessage(eventRecord, supplementalLegacy);

                if (supplementalBest is not null)
                {
                    Logger?.Debug($"{nameof(ResolveDescription)}: Disambiguated via supplemental legacy message - Provider={eventRecord.ProviderName}, EventId={eventRecord.Id}");

                    return FormatDescription(properties, supplementalBest.Text,
                        GetParametersForDescription(primaryDetails, supplemental, true, ref supplemental, eventRecord));
                }
            }
        }

        // Some events store the entire description in a single property when no template exists.
        // Only the single-property case is meaningful here; multi-property events without a template
        // cannot be rendered into a description and would just emit a misleading constant.
        if (properties.Count == 1)
        {
            Logger?.Debug($"{nameof(ResolveDescription)}: Using single-property description fallback - Provider={eventRecord.ProviderName}, EventId={eventRecord.Id}");

            return FormatDescription(properties, null, details.Parameters);
        }

        if (details.IsEmpty && (supplemental is null || supplemental.IsEmpty))
        {
            Logger?.Debug($"{nameof(ResolveDescription)}: No provider metadata available - Provider={eventRecord.ProviderName}, EventId={eventRecord.Id}, Version={eventRecord.Version}, LogName={eventRecord.LogName}, RecordId={eventRecord.RecordId}, Keywords=0x{eventRecord.Keywords ?? 0:X16}");

            return BuildNoMetadataFallbackDescription(eventRecord, properties);
        }

        Logger?.Debug($"{nameof(ResolveDescription)}: No matching description found - Provider={eventRecord.ProviderName}, EventId={eventRecord.Id}, Version={eventRecord.Version}, LogName={eventRecord.LogName}, RecordId={eventRecord.RecordId}");

        return DefaultNoMatchingDescription;
    }
}
