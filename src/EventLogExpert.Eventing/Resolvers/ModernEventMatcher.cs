// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Readers;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Resolution;

namespace EventLogExpert.Eventing.Resolvers;

/// <summary>
///     Matches an <see cref="EventRecord" /> to its <see cref="EventModel" /> in a provider's event table, and
///     disambiguates ambiguous legacy message tables by Qualifier / LogLink / severity.
/// </summary>
/// <remarks>
///     The match procedure walks several fallback paths in order: exact Id+Version+LogName + strict
///     template-property-count, then relaxed-template-count (handles version-mismatch optional fields), then Id+LogName
///     ignoring Version, then full-RawId+Qualifiers (for Complus / WPDClassInstaller entries), then short-Id+Version
///     ignoring LogName, then Id+Version ignoring LogName when exactly one candidate passes template validation.
/// </remarks>
internal sealed class ModernEventMatcher(TemplateAnalyzer templates, ITraceLogger? logger)
{
    private readonly ITraceLogger? _logger = logger;
    private readonly TemplateAnalyzer _templates = templates;

    /// <summary>
    ///     Disambiguates multiple legacy messages for the same event ID. Tries Qualifier match (high 16 bits of RawId),
    ///     then LogLink, then severity. Returns null when the set is empty or remains ambiguous after all checks.
    /// </summary>
    public static MessageModel? DisambiguateLegacyMessage(EventRecord eventRecord, IReadOnlyList<MessageModel> legacyMessages)
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

    /// <summary>
    ///     Locates the <see cref="EventModel" /> in <paramref name="details" /> whose template matches the
    ///     <paramref name="eventRecord" />, walking the documented fallback chain. Returns null when no candidate passes both
    ///     Id/Version/LogName and template-property-count checks.
    /// </summary>
    public EventModel? Match(EventRecord eventRecord, ProviderDetails details)
    {
        if (eventRecord.Version is null && string.IsNullOrEmpty(eventRecord.LogName))
        {
            _logger?.Debug($"{nameof(Match)}: Skipping modern event lookup - EventId={eventRecord.Id}, Provider={eventRecord.ProviderName} has null Version and empty LogName");

            return null;
        }

        int eventPropertyCount = eventRecord.Properties.Length;

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
            _logger?.Debug($"{nameof(Match)}: Exact match found - EventId={eventRecord.Id}, Version={eventRecord.Version}, LogName={eventRecord.LogName}");

            return modernEvent;
        }

        // For exact Id+Version+LogName matches, tolerate the template having slightly
        // more data nodes than the event supplies. This handles version mismatches where
        // the manifest added optional fields. The description formatter handles out-of-range
        // property indices gracefully by substituting empty string.
        if (modernEvent is not null && _templates.ApproximatelyMatchesPropertyCount(modernEvent.Template, eventPropertyCount))
        {
            _logger?.Debug($"{nameof(Match)}: Exact match with relaxed template count - EventId={eventRecord.Id}, Version={eventRecord.Version}, LogName={eventRecord.LogName}, PropertyCount={eventPropertyCount}");

            return modernEvent;
        }

        if (modernEvent is not null)
        {
            _logger?.Debug($"{nameof(Match)}: Exact match found but template property count mismatch - EventId={eventRecord.Id}, Version={eventRecord.Version}, LogName={eventRecord.LogName}, PropertyCount={eventPropertyCount}");
        }

        foreach (var @event in candidateEvents)
        {
            if (!LogNamesMatch(@event.LogName, eventRecord.LogName)) { continue; }

            if (!_templates.MatchesPropertyCount(@event.Template, eventPropertyCount)) { continue; }

            _logger?.Debug($"{nameof(Match)}: Match by Id/LogName with template - EventId={eventRecord.Id}, LogName={eventRecord.LogName}, MatchedVersion={@event.Version}");

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

                _logger?.Debug($"{nameof(Match)}: Match by full RawId fallback - RawId={fullRawId:X}, Version={eventRecord.Version}");

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
            _logger?.Debug($"{nameof(Match)}: Match by short Id/Version fallback - EventId={eventRecord.Id}, Version={eventRecord.Version}");

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
            _logger?.Debug($"{nameof(Match)}: Unique match by Id/Version ignoring LogName - EventId={eventRecord.Id}, Version={eventRecord.Version}, MatchedLogName={logNameIgnoredMatch.LogName}");

            return logNameIgnoredMatch;
        }

        _logger?.Debug($"{nameof(Match)}: No matching event found - EventId={eventRecord.Id}, Version={eventRecord.Version}, LogName={eventRecord.LogName}, PropertyCount={eventPropertyCount}, CandidateEventsWithSameId={candidateEvents.Count}");

        return null;
    }

    /// <summary>Treats null and empty string as equivalent for LogName comparison.</summary>
    private static bool LogNamesMatch(string? a, string? b) =>
        string.IsNullOrEmpty(a) ? string.IsNullOrEmpty(b) : string.Equals(a, b, StringComparison.Ordinal);
}
