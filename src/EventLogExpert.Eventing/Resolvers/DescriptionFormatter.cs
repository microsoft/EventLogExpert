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

/// <summary>
///     Resolves and formats the human-readable description for an event record from provider metadata, handling %N
///     positional substitution, %%N parameter resolution, %%-escape cleanup, and the no-metadata fallback path.
/// </summary>
/// <remarks>
///     <para>
///         Construction injects the unified <see cref="TemplateAnalyzer" /> (for outType extraction), the optional
///         <see cref="IEventResolverCache" /> for description / value interning, the optional <see cref="ITraceLogger" />
///         for diagnostics, and a <see cref="Func{T, TResult}" /> delegate that exposes the owning
///         <see cref="EventResolverBase" />'s protected virtual <c>TryGetSupplementalDetails</c> hook for the lazy-load
///         fallback inside <c>PickParameterSourceForDescription</c>. Legacy-message disambiguation invokes the static
///         <see cref="ModernEventMatcher.DisambiguateLegacyMessage" /> directly (no instance reference held).
///     </para>
///     <para>
///         The delegate-injection pattern captures the virtual-method slot at base-ctor time via <c>ldvirtftn</c>. When
///         derived ctors run (e.g., <see cref="EventResolver" /> override), the delegate dispatches to the override.
///         Invocation only happens during <see cref="Resolve" /> after the derived ctor body has completed, so
///         override-side fields are fully initialized.
///     </para>
///     <para>
///         Note: the <c>supplementalProvider</c> may return non-null even when the <c>supplemental</c> parameter passed
///         to <see cref="Resolve" /> is null at entry. This is the lazy backstop for the modernEvent-decisive path where
///         <c>EventResolverBase.ResolveEvent</c> short-circuits supplemental loading. Implementers must NOT assume
///         entry-supplemental-null means no supplemental exists.
///     </para>
/// </remarks>
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

    /// <summary>Resolve event descriptions from an event record.</summary>
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

        var properties = GetFormattedProperties(modernEvent?.Template, eventRecord.Properties);

        var descriptionFromSupplemental = supplemental is not null && ReferenceEquals(descriptionDetails, supplemental);

        if (!string.IsNullOrEmpty(modernEvent?.Description))
        {
            _logger?.Debug($"{nameof(Resolve)}: Using modern event description - Provider={eventRecord.ProviderName}, EventId={eventRecord.Id}, PropertyCount={properties.Count}");

            return FormatDescription(properties, modernEvent.Description,
                PickParameterSourceForDescription(modernEvent.Description, primaryDetails, descriptionFromSupplemental, ref supplemental, eventRecord));
        }

        // Legacy provider message lookup
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

            // Last-resort: ambiguous primary may be resolvable via supplemental. ResolveEvent
            // pre-loads supplemental and its modern event for count > 1, so both are already
            // set here when supplemental is available.
            if (supplemental is not null && !ReferenceEquals(supplemental, descriptionDetails))
            {
                if (!string.IsNullOrEmpty(supplementalModernEvent?.Description))
                {
                    _logger?.Debug($"{nameof(Resolve)}: Disambiguated via supplemental modern event - Provider={eventRecord.ProviderName}, EventId={eventRecord.Id}");

                    var supplementalProperties = GetFormattedProperties(supplementalModernEvent!.Template, eventRecord.Properties);

                    // Description came from supplemental, so resolve %%n parameter substitutions
                    // against supplemental's parameter table first.
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

        // Some events store the entire description in a single property when no template exists.
        // Only the single-property case is meaningful here; multi-property events without a template
        // cannot be rendered into a description and would just emit a misleading constant.
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
            // EventId 0 with the Classic keyword bit is what mmc renders as the Win32
            // ERROR_SUCCESS text ("The operation completed successfully."). The Win32
            // ERROR_SUCCESS code happens to be 0 too, but we are deliberately requesting
            // the ERROR_SUCCESS message - not treating the EventId as a Win32 error code.
            const uint Win32ErrorSuccess = 0;
            systemMessage = NativeMethods.FormatSystemMessage(Win32ErrorSuccess);
        }

        string? propertyTail = BuildEventDataTail(properties);

        // The propertyTail varies per event (timestamps, paths, IDs, etc.) - caching it
        // would grow the description cache unboundedly.
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
            // If there is only one property then this is what certain EventRecords look like
            // when the entire description is a string literal, and there is no provider DLL needed.
            // Found a few providers that have their properties wrapped with \r\n for some reason.
            // Multi-property fall-through is a defensive backstop (e.g. a legacy message row with
            // empty Text); DefaultFailedDescription is reserved for actual formatting exceptions.
            return properties.Count == 1
                ? properties[0].TrimEnd('\0', '\r', '\n')
                : DefaultNoMatchingDescription;
        }

        // Guard against stack overflow from very large templates
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
                    // %0 is a Windows Event Log message terminator - skip it entirely
                    if (propIndex == 0)
                    {
                        lastIndex = match.Index + match.Length;

                        continue;
                    }

                    if (propIndex > properties.Count)
                    {
                        _logger?.Debug($"{nameof(FormatDescription)}: Property index out of range - RequestedIndex={propIndex}, PropertyCount={properties.Count}, Template={descriptionTemplate}");

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
                            parameterSource?.GetParameterByRawId((int)parameterId)?.Text ?? string.Empty;

                        // Fallback to the cached system message table when the provider's parameter table has no
                        // entry. Caching hits AND misses keeps foreign/uninstalled providers (whose codes never
                        // resolve) from re-invoking Win32 FormatMessage on every event; an empty result still
                        // leaves the %%N token unsubstituted, matching the prior behavior.
                        if (parameterMessage.IsEmpty && parameterId is > 0 and <= uint.MaxValue)
                        {
                            parameterMessage = NativeErrorResolver.GetSystemMessageCached((uint)parameterId);
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

            // Intern the formatted description so the many events sharing an identical description (recurring event
            // types) reference one string instance. Mirrors the no-properties (~line 343) and exception (~line 466)
            // paths which already cache; the cache is bounded so high-cardinality logs cannot grow it without limit.
            return _cache?.GetOrAddDescription(returnDescription) ?? returnDescription;
        }
        catch (InvalidOperationException ex)
        {
            _logger?.Warning($"{nameof(FormatDescription)}: InvalidOperationException - PropertyCount={properties.Count}, Template={descriptionTemplate}, Exception={ex.Message}");

            // If the regex fails to match, then we just return the original description.
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

    private List<string> GetFormattedProperties(ReadOnlySpan<char> template, IReadOnlyList<object> properties)
    {
        ImmutableArray<string> dataNodes = default;
        List<string> formattedValues = new(properties.Count);

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
                        formattedValues.Add(property?.ToString() ?? string.Empty);
                    }
                    else if (s_displayAsHexTypes.Contains(outType))
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
                            _ => property?.ToString() ?? string.Empty
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
                            : property?.ToString() ?? string.Empty);
                    }
                    else
                    {
                        formattedValues.Add(property?.ToString() ?? string.Empty);
                    }

                    break;
            }

            index++;
        }

        return formattedValues;
    }

    /// <summary>
    ///     Picks the parameter source for %%n substitutions, biased toward whichever provider supplied the description
    ///     text. When <paramref name="descriptionFromSupplemental" /> is true, prefer supplemental's parameters and fall back
    ///     to primary; otherwise prefer primary and fall back to supplemental (lazily loading it when not yet available).
    ///     Short-circuits to <c>null</c> when the description has no %% tokens, avoiding the eager supplemental load on hot
    ///     paths where the lookup would never fire.
    /// </summary>
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
