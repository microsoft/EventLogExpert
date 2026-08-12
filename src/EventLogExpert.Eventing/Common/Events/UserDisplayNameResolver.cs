// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Security.Principal;

namespace EventLogExpert.Eventing.Common.Events;

/// <summary>
///     Resolves the best-available user identity for an event's User column, offline (no <c>LookupAccountSid</c>):
///     the System <c>&lt;Security UserID&gt;</c> mapped through <see cref="WellKnownSids" />, otherwise the Subject/Target
///     account names the event already carries in its &lt;EventData&gt;. Computed once at resolve time and stored on
///     <see cref="ResolvedEvent.UserDisplayName" />.
/// </summary>
internal static class UserDisplayNameResolver
{
    internal static string Resolve(SecurityIdentifier? userId, EventDataView eventData)
    {
        if (userId is not null)
        {
            string sid = userId.Value;

            if (WellKnownSids.TryGetName(sid, out string? knownName)) { return knownName; }

            // A populated but non-well-known SID is almost always a raw user SID with no offline name; prefer an
            // EventData-carried account name when one exists, else fall back to the SID (today's behavior).
            return TryDeriveFromEventData(eventData) ?? sid;
        }

        return TryDeriveFromEventData(eventData) ?? string.Empty;
    }

    private static string Format(string name, string? domain) => domain is null ? name : domain + "\\" + name;

    private static bool IsInformative(string name) =>
        !name.EndsWith('$') &&
        !string.Equals(name, "SYSTEM", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(name, "LOCAL SERVICE", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(name, "NETWORK SERVICE", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(name, "ANONYMOUS LOGON", StringComparison.OrdinalIgnoreCase);

    // Mirrors EventColumnStore.IsUsableRawValue: Windows writes "-" (or empty) for an absent name/domain.
    private static bool IsUsable(string? value) => !string.IsNullOrWhiteSpace(value) && value != "-";

    private static string? TryDeriveFromEventData(EventDataView eventData)
    {
        if (eventData.Kind == EventDataKind.None) { return null; }

        (string Name, string? Domain)? subject = TryReadAccount(eventData, "Subject");
        (string Name, string? Domain)? target = TryReadAccount(eventData, "Target");

        // Prefer the Subject only when it names a real principal (not SYSTEM / a service / a machine account), so
        // logon/auth events surface the logged-on Target rather than the reporting SYSTEM/MACHINE$ subject.
        if (subject is { } informativeSubject && IsInformative(informativeSubject.Name))
        {
            return Format(informativeSubject.Name, informativeSubject.Domain);
        }

        if (target is { } presentTarget) { return Format(presentTarget.Name, presentTarget.Domain); }

        if (subject is { } fallbackSubject) { return Format(fallbackSubject.Name, fallbackSubject.Domain); }

        return null;
    }

    private static (string Name, string? Domain)? TryReadAccount(EventDataView eventData, string prefix)
    {
        if (!eventData.TryGetValue(prefix + "UserName", out EventFieldValue nameValue)) { return null; }

        string name = nameValue.AsString();

        if (!IsUsable(name)) { return null; }

        string? domain = null;

        if (eventData.TryGetValue(prefix + "DomainName", out EventFieldValue domainValue))
        {
            string domainText = domainValue.AsString();

            if (IsUsable(domainText)) { domain = domainText; }
        }

        return (name, domain);
    }
}
