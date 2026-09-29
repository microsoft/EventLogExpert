// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization.Plural;
using EventLogExpert.Logging.Abstractions;
using System.Globalization;
using System.Resources;

namespace EventLogExpert.Localization;

public sealed class SharedResourceNeutralResolver : INeutralTextResolver
{
    private static readonly CultureInfo s_englishCulture = CultureInfo.GetCultureInfo("en");
    private static readonly IcuMessageFormatter s_formatter = new();
    private static readonly ResourceManager s_resourceManager = new(
        "EventLogExpert.Localization.Resources.SharedResource",
        typeof(SharedResource).Assembly);

    public string Resolve(string key, IReadOnlyList<string> args)
    {
        string template = s_resourceManager.GetString(key, CultureInfo.InvariantCulture) ?? key;

        if (args.Count == 0) { return template; }

        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, [.. args]);
        }
        catch (FormatException)
        {
            // Best-effort: a malformed template or argument-count mismatch must never crash the logging pipeline.
            return template;
        }
    }

    public string Resolve(string key, IReadOnlyList<string> args, long? pluralCount)
    {
        if (!pluralCount.HasValue) { return Resolve(key, args); }

        string? template = s_resourceManager.GetString(key, CultureInfo.InvariantCulture);

        if (template is null) { return key; }

        try
        {
            // Neutral path: numbers formatted invariant, plural category selected with English rules.
            return s_formatter.Format(template, PluralArgs.Build(args, pluralCount.Value), CultureInfo.InvariantCulture, s_englishCulture);
        }
        catch (IcuMessageException exception)
        {
            // Report once, then return the key - never surface raw ICU syntax in the neutral log.
            MalformedPatternReporter.ReportOnce(PluralServices.Current, key, "en", template, exception.Message);

            return key;
        }
    }
}
