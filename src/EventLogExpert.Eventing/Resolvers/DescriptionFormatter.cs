// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Resolution;
using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace EventLogExpert.Eventing.Resolvers;

// supplementalProvider can lazy-load even when entry supplemental is null; do not treat null as final.
internal sealed partial class DescriptionFormatter(
    TemplateAnalyzer templates,
    IEventResolverCache? cache,
    ITraceLogger? logger,
    Func<EventRecord, ProviderDetails?> supplementalProvider)
{
    private const string DefaultFailedDescription = "Failed to resolve description, see XML for more details.";
    private const string DefaultNoMatchingDescription = "No matching message found with loaded providers, see XML for more details.";
    private const string DefaultNoProviderDescription = "No matching provider available, see XML for more details.";

    private static readonly FrozenSet<string> s_displayAsHexTypes = new[]
    {
        "win:HexInt32",
        "win:HexInt64",
        "win:Pointer",
        "win:Win32Error"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly IEventResolverCache? _cache = cache;
    private readonly ITraceLogger? _logger = logger;
    private readonly Regex _sectionsToReplace = WildcardWithNumberRegex();
    private readonly Func<EventRecord, ProviderDetails?> _supplementalProvider = supplementalProvider;
    private readonly TemplateAnalyzer _templates = templates;

    public string Resolve(
        EventRecord eventRecord,
        ProviderDetails? primaryDetails,
        ProviderDetails? descriptionDetails,
        EventModel? modernEvent,
        ProviderDetails? supplemental,
        EventModel? supplementalModernEvent)
    {
        if (descriptionDetails is null)
        {
            _logger?.Debug($"{nameof(Resolve)}: No provider details available - Provider={eventRecord.ProviderName}, EventId={eventRecord.Id}, RecordId={eventRecord.RecordId}");

            return DefaultNoProviderDescription;
        }

        var properties = GetFormattedProperties(modernEvent?.Template, eventRecord.Properties, descriptionDetails.Maps);

        var descriptionFromSupplemental = supplemental is not null && ReferenceEquals(descriptionDetails, supplemental);

        if (!string.IsNullOrEmpty(modernEvent?.Description))
        {
            _logger?.Debug($"{nameof(Resolve)}: Using modern event description - Provider={eventRecord.ProviderName}, EventId={eventRecord.Id}, PropertyCount={properties.Count}");

            return FormatDescription(properties, modernEvent.Description,
                PickParameterSourceForDescription(modernEvent.Description, primaryDetails, descriptionFromSupplemental, ref supplemental, eventRecord));
        }

        var legacyMessages = descriptionDetails.GetMessagesByShortId(eventRecord.Id);

        if (legacyMessages.Count == 1)
        {
            _logger?.Debug($"{nameof(Resolve)}: Using legacy message - Provider={eventRecord.ProviderName}, EventId={eventRecord.Id}, PropertyCount={properties.Count}");

            return FormatDescription(properties, legacyMessages[0].Text,
                PickParameterSourceForDescription(legacyMessages[0].Text, primaryDetails, descriptionFromSupplemental, ref supplemental, eventRecord));
        }

        if (legacyMessages.Count > 1)
        {
            var bestMatch = ModernEventMatcher.DisambiguateLegacyMessage(eventRecord, legacyMessages);

            if (bestMatch is not null)
            {
                _logger?.Debug($"{nameof(Resolve)}: Disambiguated legacy message - Provider={eventRecord.ProviderName}, EventId={eventRecord.Id}, Level={eventRecord.Level}");

                return FormatDescription(properties, bestMatch.Text,
                    PickParameterSourceForDescription(bestMatch.Text, primaryDetails, descriptionFromSupplemental, ref supplemental, eventRecord));
            }

            _logger?.Debug($"{nameof(Resolve)}: Multiple legacy messages found, could not disambiguate - Provider={eventRecord.ProviderName}, EventId={eventRecord.Id}, MessageCount={legacyMessages.Count}");

            // Ambiguous primary messages may resolve through preloaded supplemental metadata.
            if (supplemental is not null && !ReferenceEquals(supplemental, descriptionDetails))
            {
                if (!string.IsNullOrEmpty(supplementalModernEvent?.Description))
                {
                    _logger?.Debug($"{nameof(Resolve)}: Disambiguated via supplemental modern event - Provider={eventRecord.ProviderName}, EventId={eventRecord.Id}");

                    var supplementalProperties = GetFormattedProperties(supplementalModernEvent!.Template, eventRecord.Properties, supplemental.Maps);

                    // Supplemental descriptions resolve %%n against supplemental parameters first.
                    return FormatDescription(supplementalProperties, supplementalModernEvent.Description,
                        PickParameterSourceForDescription(supplementalModernEvent.Description, primaryDetails, true, ref supplemental, eventRecord));
                }

                var supplementalLegacy = supplemental.GetMessagesByShortId(eventRecord.Id);
                var supplementalBest = ModernEventMatcher.DisambiguateLegacyMessage(eventRecord, supplementalLegacy);

                if (supplementalBest is not null)
                {
                    _logger?.Debug($"{nameof(Resolve)}: Disambiguated via supplemental legacy message - Provider={eventRecord.ProviderName}, EventId={eventRecord.Id}");

                    return FormatDescription(properties, supplementalBest.Text,
                        PickParameterSourceForDescription(supplementalBest.Text, primaryDetails, true, ref supplemental, eventRecord));
                }
            }
        }

        // Single-property template-less events can carry the full description; multi-property fallback would mislead.
        if (properties.Count == 1)
        {
            _logger?.Debug($"{nameof(Resolve)}: Using single-property description fallback - Provider={eventRecord.ProviderName}, EventId={eventRecord.Id}");

            return FormatDescription(properties, null, null);
        }

        if (descriptionDetails.IsEmpty && (supplemental is null || supplemental.IsEmpty))
        {
            _logger?.Debug($"{nameof(Resolve)}: No provider metadata available - Provider={eventRecord.ProviderName}, EventId={eventRecord.Id}, Version={eventRecord.Version}, LogName={eventRecord.LogName}, RecordId={eventRecord.RecordId}, Keywords=0x{eventRecord.Keywords ?? 0:X16}");

            return BuildNoMetadataFallbackDescription(eventRecord, properties);
        }

        _logger?.Debug($"{nameof(Resolve)}: No matching description found - Provider={eventRecord.ProviderName}, EventId={eventRecord.Id}, Version={eventRecord.Version}, LogName={eventRecord.LogName}, RecordId={eventRecord.RecordId}");

        return DefaultNoMatchingDescription;
    }

    private static string? BuildEventDataTail(List<string> properties)
    {
        if (properties.Count == 0) { return null; }

        const string Header = "The following information was included with the event:\r\n";

        int totalLength = Header.Length;

        for (int i = 0; i < properties.Count; i++)
        {
            totalLength += 2 + properties[i].Length;
        }

        return string.Create(totalLength, properties, static (span, props) =>
        {
            Header.AsSpan().CopyTo(span);
            int idx = Header.Length;

            for (int i = 0; i < props.Count; i++)
            {
                span[idx++] = '\r';
                span[idx++] = '\n';
                props[i].AsSpan().CopyTo(span[idx..]);
                idx += props[i].Length;
            }
        });
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

    private static string FormatDisplayAsHex(EventProperty property) => property.Kind switch
    {
        EventPropertyKind.Byte => $"0x{property.AsByte:X}",
        EventPropertyKind.SByte => $"0x{property.AsSByte:X}",
        EventPropertyKind.Int16 => $"0x{property.AsInt16:X}",
        EventPropertyKind.UInt16 => $"0x{property.AsUInt16:X}",
        EventPropertyKind.Int32 => $"0x{property.AsInt32:X}",
        EventPropertyKind.UInt32 => $"0x{property.AsUInt32:X}",
        EventPropertyKind.Int64 => $"0x{property.AsInt64:X}",
        EventPropertyKind.UInt64 => $"0x{property.AsUInt64:X}",
        _ => FormatNumericToString(property)
    };

    private static string FormatNtStatus(EventProperty property)
    {
        uint statusCode;

        switch (property.Kind)
        {
            case EventPropertyKind.UInt32: statusCode = property.AsUInt32; break;
            case EventPropertyKind.Int32: statusCode = (uint)property.AsInt32; break;
            case EventPropertyKind.UInt64: statusCode = (uint)property.AsUInt64; break;
            case EventPropertyKind.Int64: statusCode = (uint)property.AsInt64; break;
            case EventPropertyKind.UInt16: statusCode = property.AsUInt16; break;
            case EventPropertyKind.Int16: statusCode = (uint)property.AsInt16; break;
            case EventPropertyKind.Byte: statusCode = property.AsByte; break;
            default: return FormatNumericToString(property);
        }

        return NativeErrorResolver.GetNtStatusMessage(statusCode);
    }

    private static string FormatNumericProperty(
        EventProperty property,
        string? outType,
        string? mapName,
        IReadOnlyDictionary<string, ValueMapDefinition> maps)
    {
        if (!string.IsNullOrEmpty(mapName) &&
            maps.TryGetValue(mapName, out ValueMapDefinition? mapDefinition) &&
            property.TryGetUnsignedBits(out ulong bits) &&
            mapDefinition.TryDecodeBits(bits, out string decodedValue))
        {
            return decodedValue;
        }

        if (string.IsNullOrEmpty(outType))
        {
            return FormatNumericToString(property);
        }

        if (s_displayAsHexTypes.Contains(outType))
        {
            return FormatDisplayAsHex(property);
        }

        if (string.Equals(outType, "win:HResult", StringComparison.OrdinalIgnoreCase) &&
            property.Kind == EventPropertyKind.Int32)
        {
            return NativeErrorResolver.GetErrorMessage((uint)property.AsInt32);
        }

        if (string.Equals(outType, "win:NTStatus", StringComparison.OrdinalIgnoreCase))
        {
            return FormatNtStatus(property);
        }

        return FormatNumericToString(property);
    }

    private static string FormatNumericToString(EventProperty property) => property.Kind switch
    {
        EventPropertyKind.SByte => property.AsSByte.ToString(),
        EventPropertyKind.Byte => property.AsByte.ToString(),
        EventPropertyKind.Int16 => property.AsInt16.ToString(),
        EventPropertyKind.UInt16 => property.AsUInt16.ToString(),
        EventPropertyKind.Int32 => property.AsInt32.ToString(),
        EventPropertyKind.UInt32 => property.AsUInt32.ToString(),
        EventPropertyKind.Int64 => property.AsInt64.ToString(),
        EventPropertyKind.UInt64 => property.AsUInt64.ToString(),
        EventPropertyKind.Single => property.AsSingle.ToString(),
        EventPropertyKind.Double => property.AsDouble.ToString(),
        EventPropertyKind.SizeT => property.AsSizeT.ToString(),
        _ => string.Empty
    };

    private static string FormatProperty(
        EventProperty property,
        string? outType,
        string? mapName,
        IReadOnlyDictionary<string, ValueMapDefinition> maps) => property.Kind switch
    {
        EventPropertyKind.Boolean => property.AsBoolean ? "true" : "false",
        EventPropertyKind.DateTime => property.AsDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffff00K"),
        EventPropertyKind.Reference => FormatReferenceProperty(property.Reference),
        _ => FormatNumericProperty(property, outType, mapName, maps)
    };

    private static string FormatReferenceProperty(object? reference) => reference switch
    {
        byte[] bytes => Convert.ToHexString(bytes),
        SecurityIdentifier sid => sid.Value,
        // Windows EvtFormatMessage renders GUID properties wrapped in braces.
        Guid guidValue => guidValue.ToString("B"),
        _ => reference?.ToString() ?? string.Empty
    };

    private static void ResizeBuffer(ref char[] buffer, ref Span<char> source, int sizeToAdd)
    {
        char[] newBuffer = ArrayPool<char>.Shared.Rent(source.Length + sizeToAdd);
        source.CopyTo(newBuffer);
        ArrayPool<char>.Shared.Return(buffer);
        source = buffer = newBuffer;
    }

    [GeneratedRegex("%+[0-9]+")]
    private static partial Regex WildcardWithNumberRegex();

    private string BuildNoMetadataFallbackDescription(EventRecord eventRecord, List<string> properties)
    {
        const long ClassicKeywordBit = 0x0080000000000000L;
        bool isClassic = ((eventRecord.Keywords ?? 0) & ClassicKeywordBit) != 0;

        string? systemMessage = null;

        if (isClassic && eventRecord.Id == 0)
        {
            // Classic EventId 0 maps to the Win32 ERROR_SUCCESS message, not arbitrary Win32 error translation.
            const uint Win32ErrorSuccess = 0;
            systemMessage = NativeMethods.FormatSystemMessage(Win32ErrorSuccess);
        }

        string? propertyTail = BuildEventDataTail(properties);

        // Do not cache property tails; per-event values would make the description cache unbounded.
        if (propertyTail is not null)
        {
            return string.IsNullOrWhiteSpace(systemMessage)
                ? propertyTail
                : $"{systemMessage}\r\n\r\n{propertyTail}";
        }

        if (string.IsNullOrWhiteSpace(systemMessage))
        {
            return DefaultNoProviderDescription;
        }

        return _cache?.GetOrAddDescription(systemMessage!) ?? systemMessage!;
    }

    private string FormatDescription(
        List<string> properties,
        string? descriptionTemplate,
        ProviderDetails? parameterSource)
    {
        string returnDescription;

        if (string.IsNullOrWhiteSpace(descriptionTemplate))
        {
            // Single-property empty-template events can be literal descriptions; multi-property cases are not renderable.
            return properties.Count == 1
                ? properties[0].TrimEnd('\0', '\r', '\n')
                : DefaultNoMatchingDescription;
        }

        const int MaxStackAllocChars = 4096;
        int cleanupBufferSize = descriptionTemplate.Length * 2;
        char[]? cleanupRented = null;

        Span<char> description = cleanupBufferSize <= MaxStackAllocChars
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
                    // %0 is a Windows Event Log message terminator.
                    if (propIndex == 0)
                    {
                        lastIndex = match.Index + match.Length;

                        continue;
                    }

                    if (propIndex > properties.Count)
                    {
                        _logger?.Debug($"{nameof(FormatDescription)}: Property index out of range - RequestedIndex={propIndex}, PropertyCount={properties.Count}, Template={descriptionTemplate}");

                        // Missing optional/versioned properties substitute empty text so remaining positions stay aligned.
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
                        // Large parameter ids may be negative signed values encoded in an unsigned token.
                        ReadOnlySpan<char> parameterMessage =
                            parameterSource?.GetParameterByRawId(unchecked((int)parameterId))?.Text ?? string.Empty;

                        // Cache system-message hits and misses so unresolved foreign-provider parameters do not call Win32 per event.
                        if (parameterMessage.IsEmpty && parameterId is > 0 and <= uint.MaxValue)
                        {
                            parameterMessage = NativeErrorResolver.GetSystemMessageCached((uint)parameterId);
                        }

                        if (!parameterMessage.IsEmpty)
                        {
                            // Remove only the exact trailing "%0" terminator.
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

            // Intern repeated formatted descriptions; the bounded cache prevents high-cardinality growth.
            return _cache?.GetOrAddDescription(returnDescription) ?? returnDescription;
        }
        catch (InvalidOperationException ex)
        {
            _logger?.Warning($"{nameof(FormatDescription)}: InvalidOperationException - PropertyCount={properties.Count}, Template={descriptionTemplate}, Exception={ex.Message}");

            returnDescription = description.ToString();

            return _cache?.GetOrAddDescription(returnDescription) ?? returnDescription;
        }
        catch (Exception ex)
        {
            _logger?.Warning($"{nameof(FormatDescription)}: Unexpected exception - PropertyCount={properties.Count}, Template={descriptionTemplate}, Exception={ex}");

            return DefaultFailedDescription;
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);

            if (cleanupRented is not null) { ArrayPool<char>.Shared.Return(cleanupRented); }
        }
    }

    private List<string> GetFormattedProperties(
        ReadOnlySpan<char> template,
        IReadOnlyList<EventProperty> properties,
        IReadOnlyDictionary<string, ValueMapDefinition> maps)
    {
        ImmutableArray<string> dataNodes = default;
        ImmutableArray<string> mapNodes = default;
        List<string> formattedValues = new(properties.Count);

        if (!template.IsEmpty)
        {
            // Match outTypes to actual property count because EvtRender may omit hidden length-provider fields.
            var meta = _templates.Analyze(template);

            if (meta.VisibleOutTypes.Length == properties.Count)
            {
                dataNodes = meta.VisibleOutTypes;
                mapNodes = meta.VisibleMaps;
            }
            else if (meta.AllOutTypes.Length == properties.Count)
            {
                dataNodes = meta.AllOutTypes;
                mapNodes = meta.AllMaps;
            }
        }

        int index = 0;

        foreach (EventProperty property in properties)
        {
            string? outType = !dataNodes.IsDefault && index < dataNodes.Length ? dataNodes[index] : null;
            string? mapName = !mapNodes.IsDefault && index < mapNodes.Length ? mapNodes[index] : null;

            formattedValues.Add(FormatProperty(property, outType, mapName, maps));
            index++;
        }

        return formattedValues;
    }

    // Bias %%n lookup toward the provider that supplied the description, lazy-loading supplemental only when needed.
    private ProviderDetails? PickParameterSourceForDescription(
        string? descriptionTemplate,
        ProviderDetails? primary,
        bool descriptionFromSupplemental,
        ref ProviderDetails? supplemental,
        EventRecord eventRecord)
    {
        if (descriptionTemplate is null || descriptionTemplate.IndexOf("%%", StringComparison.Ordinal) < 0)
        {
            return null;
        }

        if (descriptionFromSupplemental && supplemental is not null)
        {
            if (supplemental.Parameters.Count > 0) { return supplemental; }

            return primary ?? supplemental;
        }

        if (primary is not null && primary.Parameters.Count > 0) { return primary; }

        supplemental ??= _supplementalProvider(eventRecord);

        return supplemental ?? primary;
    }
}
