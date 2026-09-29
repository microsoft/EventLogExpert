// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Concurrent;

namespace EventLogExpert.Localization.Plural;

/// <summary>
///     Generation-scoped, once-per-(key, culture) gate for malformed-plural-pattern diagnostics. The plural render
///     boundaries (the UI <c>PluralText</c> facade and the deferred-log text resolvers) share this reporter so a
///     repeatedly rendered malformed pattern notifies the configured <see cref="IPluralDiagnostics" /> sink at most once
///     per (configuration generation, key, culture) - honoring the sink's "callers de-duplicate" contract. Reporting is
///     best-effort: a throwing sink is swallowed so a faulty diagnostics implementation can never escape into a render
///     path. Keying on <see cref="PluralServices.Snapshot.Generation" /> lets a reconfiguration (or the test
///     <see cref="Reset" /> seam) re-notify.
/// </summary>
public static class MalformedPatternReporter
{
    private static readonly ConcurrentDictionary<(int Generation, string Key, string Culture), byte> s_reported = new();

    /// <summary>
    ///     Reports the malformed pattern to <paramref name="services" />'s diagnostics sink unless an identical
    ///     (generation, key, culture) triple has already been reported. Never throws.
    /// </summary>
    public static void ReportOnce(
        PluralServices.Snapshot services,
        string key,
        string cultureName,
        string pattern,
        string error)
    {
        if (!s_reported.TryAdd((services.Generation, key, cultureName), 0))
        {
            return;
        }

        try
        {
            services.Diagnostics.ReportMalformedPattern(key, cultureName, pattern, error);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            // A throwing diagnostics sink must not escape the render path this gate protects.
        }
    }

    /// <summary>Test seam: forgets prior reports so a freshly configured sink is notified again.</summary>
    internal static void Reset() => s_reported.Clear();
}
