// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Globalization;

namespace EventLogExpert.Localization.Plural;

/// <summary>
///     Builds the ICU argument dictionary for a deferred-log plural site whose arguments arrive as a positional
///     string list plus a typed numeric selector. Positional entries map to <c>"0"</c>, <c>"1"</c>, ... (referenced as
///     <c>{0}</c>, <c>{1}</c>) and the selector maps to <c>"count"</c> (referenced as <c>{count, plural, ...}</c>).
/// </summary>
public static class PluralArgs
{
    public static IReadOnlyDictionary<string, object?> Build(IReadOnlyList<string>? positionalArgs, long pluralCount)
    {
        Dictionary<string, object?> result = new(StringComparer.Ordinal) { ["count"] = pluralCount };

        if (positionalArgs is not null)
        {
            for (int index = 0; index < positionalArgs.Count; index++)
            {
                result[index.ToString(CultureInfo.InvariantCulture)] = positionalArgs[index];
            }
        }

        return result;
    }
}
