// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Globalization;
using System.Resources;

namespace EventLogExpert.Localization.Plural;

/// <summary>
///     Supplies the RAW neutral (invariant/English) resource pattern for a key, so a UI boundary that fails to format
///     a translated pattern can fall back to the neutral English pattern (rendered with English plural rules) rather than
///     exposing raw ICU syntax. Distinct from an <c>IStringLocalizer</c>, which resolves the ambient UI culture.
/// </summary>
public interface INeutralPatternProvider
{
    string? GetNeutralPattern(string key);
}

/// <summary>
///     The pre-configuration default: reads the neutral <see cref="SharedResource" /> values via its resource
///     manager.
/// </summary>
public sealed class SharedResourceNeutralPatternProvider : INeutralPatternProvider
{
    private static readonly ResourceManager s_resourceManager = new(
        "EventLogExpert.Localization.Resources.SharedResource",
        typeof(SharedResource).Assembly);

    public string? GetNeutralPattern(string key) => s_resourceManager.GetString(key, CultureInfo.InvariantCulture);
}
