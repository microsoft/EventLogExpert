// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using System.Collections.Frozen;
using System.Globalization;

namespace EventLogExpert.Runtime.DetailsPane;

/// <summary>
///     Resolves optional inline explanations for structured EventData fields: a small curated glossary (what a field
///     means) plus a bounded set of value decoders (what a specific value means, e.g. a logon type). Both are fail-closed
///     - an unrecognized field or value yields no text rather than a guess. Glossary lookups follow a most-specific-wins
///     order (provider + event id + field, then provider + field, then a small allowlist of provider-agnostic field
///     names); decoders and the glossary resolve independently, so a value decode is never suppressed by a glossary match
///     or vice versa.
/// </summary>
public static class EventFieldExplainer
{
    private const string SecurityAuditing = "Microsoft-Windows-Security-Auditing";

    // Field names whose meaning is too provider-specific to explain from a bare name. They are only ever explained
    // through a provider- or event-scoped rule, never the provider-agnostic allowlist.
    private static readonly FrozenSet<string> s_ambiguousFieldNames =
        new[] { "Status", "Type", "Name", "Id", "Subject", "Data" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly GlossaryEntry[] s_glossary =
    [
        // Provider + event scoped (most specific).
        new(SecurityAuditing, 4624, "TargetUserName", "The account that was logged on."),
        new(SecurityAuditing, 4624, "SubjectUserName", "The account that requested the logon."),
        new(SecurityAuditing, 4625, "TargetUserName", "The account that failed to log on."),

        // Provider scoped (any event from this provider).
        new(SecurityAuditing, null, "AuthenticationPackageName", "The authentication package (e.g. NTLM, Kerberos) that handled the logon."),
        new(SecurityAuditing, null, "LogonProcessName", "The trusted process that submitted the logon request."),

        // Provider-agnostic allowlist (field names whose meaning is stable across providers).
        new(null, null, "LogonType", "How the logon was initiated; see the decoded value for the specific type."),
        new(null, null, "IpAddress", "The source network address associated with the event."),
        new(null, null, "IpPort", "The source network port associated with the event."),
        new(null, null, "ProcessName", "The full path of the process associated with the event."),
        new(null, null, "CommandLine", "The command line used to start the process."),
        new(null, null, "WorkstationName", "The name of the workstation from which the action originated.")
    ];

    private static readonly FrozenDictionary<string, Func<EventFieldValue, string?>> s_valueDecoders =
        new Dictionary<string, Func<EventFieldValue, string?>>
        {
            ["LogonType"] = DecodeLogonType
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Produces an <see cref="EventFieldExplanation" /> for the given field, resolving a value decode and a glossary
    ///     description independently. Returns <c>false</c> (and an empty explanation) when neither applies.
    /// </summary>
    public static bool TryExplain(
        string providerName,
        int eventId,
        string fieldName,
        in EventFieldValue value,
        out EventFieldExplanation explanation)
    {
        string? decodedLabel = TryDecodeValue(fieldName, value);
        string? description = ResolveDescription(providerName, eventId, fieldName);

        explanation = new EventFieldExplanation(decodedLabel, description);

        return explanation.HasValue;
    }

    private static string? DecodeLogonType(EventFieldValue value)
    {
        if (!TryReadWholeNumber(value, out ulong logonType)) { return null; }

        return logonType switch
        {
            0 => "System",
            2 => "Interactive",
            3 => "Network",
            4 => "Batch",
            5 => "Service",
            7 => "Unlock",
            8 => "NetworkCleartext",
            9 => "NewCredentials",
            10 => "RemoteInteractive",
            11 => "CachedInteractive",
            12 => "CachedRemoteInteractive",
            13 => "CachedUnlock",
            _ => null
        };
    }

    private static string? ResolveDescription(string providerName, int eventId, string fieldName)
    {
        // Most specific: provider + event id + field.
        foreach (GlossaryEntry entry in s_glossary)
        {
            if (entry.EventId == eventId
                && entry.Provider is { } scopedProvider
                && string.Equals(scopedProvider, providerName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(entry.Field, fieldName, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Description;
            }
        }

        // Provider + field (any event from that provider).
        foreach (GlossaryEntry entry in s_glossary)
        {
            if (entry.EventId is null
                && entry.Provider is { } scopedProvider
                && string.Equals(scopedProvider, providerName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(entry.Field, fieldName, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Description;
            }
        }

        // Provider-agnostic allowlist, never applied to an ambiguous bare name.
        if (s_ambiguousFieldNames.Contains(fieldName)) { return null; }

        foreach (GlossaryEntry entry in s_glossary)
        {
            if (entry is { Provider: null, EventId: null }
                && string.Equals(entry.Field, fieldName, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Description;
            }
        }

        return null;
    }

    private static string? TryDecodeValue(string fieldName, in EventFieldValue value) =>
        s_valueDecoders.TryGetValue(fieldName, out Func<EventFieldValue, string?>? decoder)
            ? decoder(value)
            : null;

    private static bool TryReadWholeNumber(in EventFieldValue value, out ulong number)
    {
        if (value.TryGetUInt64(out number)) { return true; }

        if (value.TryGetInt64(out long signed) && signed >= 0)
        {
            number = (ulong)signed;

            return true;
        }

        // Strict parse only for a string-typed value: no sign, whitespace, or fractional part.
        if (value.Kind == EventFieldValueKind.String
            && ulong.TryParse(value.AsString(), NumberStyles.None, CultureInfo.InvariantCulture, out number))
        {
            return true;
        }

        number = 0;

        return false;
    }

    private sealed record GlossaryEntry(string? Provider, int? EventId, string Field, string Description);
}
