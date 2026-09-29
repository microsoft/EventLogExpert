// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Localization.Plural;

/// <summary>
///     Sink for malformed-plural-pattern diagnostics. The caller (which holds the resource key) is responsible for
///     once-per-(key, culture) rate limiting before reporting, so an implementation may assume each call is already
///     de-duplicated. Implementations must be non-localized and must never resolve text through the plural mechanism (no
///     recursion).
/// </summary>
public interface IPluralDiagnostics
{
    void ReportMalformedPattern(string key, string cultureName, string pattern, string error);
}

/// <summary>The pre-configuration default: discards diagnostics.</summary>
public sealed class NoOpPluralDiagnostics : IPluralDiagnostics
{
    public static readonly NoOpPluralDiagnostics Instance = new();

    private NoOpPluralDiagnostics() { }

    public void ReportMalformedPattern(string key, string cultureName, string pattern, string error) { }
}
