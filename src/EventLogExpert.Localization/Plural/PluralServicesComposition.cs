// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Localization.Plural;

public static class PluralServicesComposition
{
    public static void ConfigureFromLogger(ITraceLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        PluralServices.TryConfigure(new TraceLoggerPluralDiagnostics(logger), new SharedResourceNeutralPatternProvider());
    }
}

internal sealed class TraceLoggerPluralDiagnostics(ITraceLogger logger) : IPluralDiagnostics
{
    public void ReportMalformedPattern(string key, string cultureName, string pattern, string error) =>
        logger.Warning($"Malformed plural pattern for resource key '{key}' (culture '{cultureName}'): {error}. Falling back to the neutral pattern or resource key.");
}
