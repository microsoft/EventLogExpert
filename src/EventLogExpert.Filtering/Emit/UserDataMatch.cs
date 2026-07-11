// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Structured;
using EventLogExpert.Filtering.Lowering;

namespace EventLogExpert.Filtering.Emit;

/// <summary>
///     The value-level tri-state UserData evaluators, shared verbatim by the row <see cref="Emitter" /> and the
///     column <see cref="ColumnEmitter" />. Each operates purely on a <see cref="StructuredFieldResult" /> (the present
///     values plus the truncation flag) so a truncated non-matching field surfaces <see cref="FilterMatch.Unknown" />
///     rather than a decisive no-match, independent of how the field was read.
/// </summary>
internal static class UserDataMatch
{
    public static Func<StructuredFieldResult, FilterMatch> Comparison(UserDataComparisonNode node)
    {
        var literal = node.Literal;

        if (node.Op == FilterBinaryOperator.Equal)
        {
            return result =>
            {
                var values = result.PresentValues;

                for (var i = 0; i < values.Length; i++)
                {
                    if (string.Equals(values[i], literal, StringComparison.Ordinal)) { return FilterMatch.Match; }
                }

                return result.IsTruncated ? FilterMatch.Unknown : FilterMatch.NoMatch;
            };
        }

        return result =>
        {
            var values = result.PresentValues;

            if (values.Length == 0) { return result.IsTruncated ? FilterMatch.Unknown : FilterMatch.NoMatch; }

            for (var i = 0; i < values.Length; i++)
            {
                if (string.Equals(values[i], literal, StringComparison.Ordinal)) { return FilterMatch.NoMatch; }
            }

            return result.IsTruncated ? FilterMatch.Unknown : FilterMatch.Match;
        };
    }

    public static Func<StructuredFieldResult, FilterMatch> Contains(UserDataContainsNode node)
    {
        var needle = node.Needle;
        var comparison = node.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (!node.Negated)
        {
            return result =>
            {
                var values = result.PresentValues;

                for (var i = 0; i < values.Length; i++)
                {
                    if (values[i].Contains(needle, comparison)) { return FilterMatch.Match; }
                }

                return result.IsTruncated ? FilterMatch.Unknown : FilterMatch.NoMatch;
            };
        }

        return result =>
        {
            var values = result.PresentValues;

            if (values.Length == 0) { return result.IsTruncated ? FilterMatch.Unknown : FilterMatch.NoMatch; }

            for (var i = 0; i < values.Length; i++)
            {
                if (values[i].Contains(needle, comparison)) { return FilterMatch.NoMatch; }
            }

            return result.IsTruncated ? FilterMatch.Unknown : FilterMatch.Match;
        };
    }

    public static Func<StructuredFieldResult, FilterMatch> MultiContains(UserDataMultiContainsNode node)
    {
        var needles = node.Needles;
        var comparison = node.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        return result =>
        {
            var values = result.PresentValues;

            for (var i = 0; i < values.Length; i++)
            {
                var value = values[i];

                for (var j = 0; j < needles.Count; j++)
                {
                    if (value.Contains(needles[j], comparison)) { return FilterMatch.Match; }
                }
            }

            return result.IsTruncated ? FilterMatch.Unknown : FilterMatch.NoMatch;
        };
    }

    public static Func<StructuredFieldResult, FilterMatch> MultiEquals(UserDataMultiEqualsNode node)
    {
        var literals = node.Literals;

        return result =>
        {
            var values = result.PresentValues;

            for (var i = 0; i < values.Length; i++)
            {
                var value = values[i];

                for (var j = 0; j < literals.Count; j++)
                {
                    if (string.Equals(value, literals[j], StringComparison.Ordinal)) { return FilterMatch.Match; }
                }
            }

            return result.IsTruncated ? FilterMatch.Unknown : FilterMatch.NoMatch;
        };
    }
}
