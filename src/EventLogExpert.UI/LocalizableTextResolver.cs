// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization.Plural;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.UI.Globalization;
using Microsoft.Extensions.Localization;
using System.Globalization;

namespace EventLogExpert.UI;

internal static class LocalizableTextResolver
{
    private static readonly CultureInfo s_englishCulture = CultureInfo.GetCultureInfo("en");
    private static readonly IcuMessageFormatter s_formatter = new();

    public static string Resolve(IStringLocalizer localizer, LocalizableText text) =>
        Resolve(localizer, text.Key, text.Args, text.Key, text.PluralCount);

    public static string Resolve(IStringLocalizer localizer, string? key, IReadOnlyList<string>? args, string english, long? pluralCount = null)
    {
        if (string.IsNullOrEmpty(key)) { return english; }

        if (pluralCount.HasValue)
        {
            IReadOnlyDictionary<string, object?> arguments = PluralArgs.Build(args, pluralCount.Value);
            CultureInfo numberCulture = CultureInfo.CurrentCulture;
            CultureInfo uiCulture = CultureInfo.CurrentUICulture;
            CultureInfo pluralCulture = ContentCulture.Resolve(uiCulture, ContentCulture.SupportedUiCultures);
            LocalizedString pattern = localizer[key];

            if (pattern.ResourceNotFound) { return english; }

            try
            {
                return s_formatter.Format(pattern.Value, arguments, numberCulture, pluralCulture);
            }
            catch (IcuMessageException exception)
            {
                // Never surface raw ICU or the bare key: report once (keyed on the resource-fetch culture), render the
                // neutral English pattern, then fall back to the key.
                PluralServices.Snapshot services = PluralServices.Current;
                MalformedPatternReporter.ReportOnce(services, key, uiCulture.Name, pattern.Value, exception.Message);

                return FormatNeutralFallback(services, key, arguments, numberCulture) ?? english;
            }
        }

        try
        {
            var localized = localizer[key, [.. args ?? []]];

            return localized.ResourceNotFound ? english : localized.Value;
        }
        catch (FormatException)
        {
            return english;
        }
    }

    private static string? FormatNeutralFallback(
        PluralServices.Snapshot services,
        string key,
        IReadOnlyDictionary<string, object?> arguments,
        CultureInfo numberCulture)
    {
        string? neutralPattern;

        try
        {
            neutralPattern = services.NeutralPatternProvider.GetNeutralPattern(key);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }

        if (neutralPattern is null) { return null; }

        try
        {
            return s_formatter.Format(neutralPattern, arguments, numberCulture, s_englishCulture);
        }
        catch (IcuMessageException)
        {
            return null;
        }
    }
}
