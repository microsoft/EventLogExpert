// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Globalization;

namespace EventLogExpert.UI.Globalization;

/// <summary>
///     Resolves the renderable content culture and its layout direction from the RESOLVED (not raw OS) culture, so an
///     RTL OS never mirrors the English layout until a translation ships.
/// </summary>
public static class ContentCulture
{
    /// <summary>
    ///     Cultures with a shipped translation; grows ONLY with a translation + the RTL prerequisite bundle (a
    ///     drift-guard test enforces alignment with the embedded satellites).
    /// </summary>
    public static readonly IReadOnlySet<string> SupportedUiCultures =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "en" };

    /// <summary>The BCP-47 layout direction for <paramref name="culture" />: <c>"rtl"</c> or <c>"ltr"</c>.</summary>
    public static string DirectionOf(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return culture.TextInfo.IsRightToLeft ? "rtl" : "ltr";
    }

    /// <summary>
    ///     The nearest supported culture for <paramref name="current" /> (walking parents, terminating at the invariant
    ///     culture), else the neutral <c>en</c>.
    /// </summary>
    public static CultureInfo Resolve(CultureInfo current, IReadOnlySet<string> supported)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(supported);

        for (var candidate = current; candidate.Name.Length > 0; candidate = candidate.Parent)
        {
            if (supported.Contains(candidate.Name)) { return candidate; }
        }

        return CultureInfo.GetCultureInfo("en");
    }
}
