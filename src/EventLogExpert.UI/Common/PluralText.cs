// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Localization.Plural;
using EventLogExpert.UI.Globalization;
using Microsoft.Extensions.Localization;
using System.Globalization;

namespace EventLogExpert.UI.Common;

/// <summary>
///     The UI-side plural facade (replaces <c>LocalizedCount</c>). Called only at known plural sites, so it always
///     routes the resolved <see cref="SharedResource" /> pattern through the in-house ICU formatter, selecting the plural
///     category on the RESOLVED content culture and formatting numbers on the current culture. A malformed pattern never
///     throws into the UI: it is reported once per (key, culture), then rendered from the neutral English pattern (with
///     English plural rules), and finally falls back to the key - never raw ICU syntax.
/// </summary>
internal static class PluralText
{
    private static readonly CultureInfo s_englishCulture = CultureInfo.GetCultureInfo("en");
    private static readonly IcuMessageFormatter s_formatter = new();

    internal static string Format(
        IStringLocalizer<SharedResource> localizer,
        string key,
        params (string Name, object? Value)[] args)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(key);

        Dictionary<string, object?> arguments = BuildArguments(args);
        CultureInfo numberCulture = CultureInfo.CurrentCulture;
        CultureInfo uiCulture = CultureInfo.CurrentUICulture;
        CultureInfo pluralCulture = ContentCulture.Resolve(uiCulture, ContentCulture.SupportedUiCultures);
        string pattern = localizer[key].Value;

        try
        {
            return s_formatter.Format(pattern, arguments, numberCulture, pluralCulture);
        }
        catch (IcuMessageException exception)
        {
            // Never throw into the UI: report (best-effort), render the neutral English pattern, then fall back to the key.
            PluralServices.Snapshot services = PluralServices.Current;
            MalformedPatternReporter.ReportOnce(services, key, uiCulture.Name, pattern, exception.Message);

            return FormatNeutralFallback(services, key, arguments, numberCulture) ?? key;
        }
    }

    private static Dictionary<string, object?> BuildArguments((string Name, object? Value)[] args)
    {
        Dictionary<string, object?> result = new(StringComparer.Ordinal);

        foreach ((string name, object? value) in args)
        {
            result[name] = value;
        }

        return result;
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
            // A faulty neutral provider must not crash the UI render this facade exists to protect.
            return null;
        }

        if (neutralPattern is null)
        {
            return null;
        }

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
