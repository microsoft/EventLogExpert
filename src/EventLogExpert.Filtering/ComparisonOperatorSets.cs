// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Filtering;

/// <summary>
///     Static membership predicates over <see cref="EventProperty" /> / <see cref="ComparisonOperator" /> /
///     <see cref="MatchMode" /> used by both the editor (to gate which dropdown entries to show) and by the codegen +
///     decomposer (to reject conditions outside the supported vocabulary).
/// </summary>
public static class ComparisonOperatorSets
{
    /// <summary>
    ///     Free-form text fields whose values are not enumerable from the active logs; the editor restricts these to
    ///     <see cref="MatchMode.Single" /> and the codegen rejects <see cref="MatchMode.Many" /> for them.
    /// </summary>
    public static bool IsTextOnly(EventProperty property) =>
        property is EventProperty.Description or EventProperty.Xml;

    /// <summary>Whether the supplied property supports <see cref="MatchMode.Many" /> (multi-value comparisons).</summary>
    public static bool SupportsMany(EventProperty property) => !IsTextOnly(property);
}
