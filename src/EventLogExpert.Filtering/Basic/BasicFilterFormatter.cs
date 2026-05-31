// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Common.Filtering;
using System.Text;

namespace EventLogExpert.Filtering.Basic;

public static class BasicFilterFormatter
{
    public static bool TryFormat(BasicFilter basicFilter, out string comparison) =>
        TryFormat(basicFilter, false, out comparison);

    public static bool TryFormat(BasicFilter basicFilter, bool strictPredicates, out string comparison)
    {
        ArgumentNullException.ThrowIfNull(basicFilter);

        comparison = string.Empty;

        if (!TryFormatComparison(basicFilter.Comparison, null, out var comparisonText))
        {
            return false;
        }

        StringBuilder stringBuilder = new(comparisonText);

        foreach (var predicate in basicFilter.Predicates)
        {
            var joinPrefix = predicate.JoinWithAny ? " || " : " && ";

            if (TryFormatComparison(predicate.Comparison, joinPrefix, out var subText))
            {
                stringBuilder.Append(subText);
            }
            else if (strictPredicates)
            {
                return false;
            }
        }

        comparison = stringBuilder.ToString();

        return true;
    }

    public static bool TryFormatComparison(FilterComparison comparison, string? joinPrefix, out string formatted)
    {
        ArgumentNullException.ThrowIfNull(comparison);

        formatted = string.Empty;

        if (comparison.MatchMode == MatchMode.Single && string.IsNullOrWhiteSpace(comparison.Value))
        {
            return false;
        }

        if (comparison is { MatchMode: MatchMode.Many, Values.Count: <= 0 })
        {
            return false;
        }

        StringBuilder stringBuilder = new(joinPrefix ?? string.Empty);

        // The Many+non-Keywords shape is `(new[]{...}).Contains(property)` — the property-reference template
        // moves to the suffix. Everything else prepends the operator+property template.
        if (comparison.MatchMode != MatchMode.Many || comparison.Property is EventProperty.Keywords)
        {
            stringBuilder.Append(GetComparisonString(comparison.Property, comparison.Operator, comparison.MatchMode));
        }

        if (comparison.MatchMode == MatchMode.Many)
        {
            EmitManyValues(comparison, stringBuilder);
        }
        else if (!EmitSingleValue(comparison, stringBuilder))
        {
            return false;
        }

        if (comparison is { MatchMode: MatchMode.Many, Property: not EventProperty.Keywords })
        {
            stringBuilder.Append(GetComparisonString(comparison.Property, comparison.Operator, comparison.MatchMode));
        }

        formatted = stringBuilder.ToString();

        return true;
    }

    private static void EmitManyValues(FilterComparison comparison, StringBuilder stringBuilder)
    {
        var joined = string.Join("\", \"", comparison.Values.Select(EscapeStringLiteral));

        if (comparison.Property is EventProperty.Keywords)
        {
            stringBuilder.Append($"(e => (new[] {{\"{joined}\"}}).Contains(e))");
        }
        else
        {
            stringBuilder.Append($"(new[] {{\"{joined}\"}}).Contains(");
        }
    }

    private static bool EmitSingleValue(FilterComparison comparison, StringBuilder stringBuilder)
    {
        switch (comparison.Operator)
        {
            case ComparisonOperator.Equals:
            case ComparisonOperator.NotEqual:
                if (comparison.Property is EventProperty.Keywords)
                {
                    stringBuilder.Append(
                        $"\"{EscapeStringLiteral(comparison.Value)}\", StringComparison.OrdinalIgnoreCase))");
                }
                else
                {
                    stringBuilder.Append($"\"{EscapeStringLiteral(comparison.Value)}\"");
                }

                return true;
            case ComparisonOperator.Contains:
            case ComparisonOperator.NotContains:
                if (comparison.Property is EventProperty.Keywords)
                {
                    stringBuilder.Append(
                        $"(\"{EscapeStringLiteral(comparison.Value)}\", StringComparison.OrdinalIgnoreCase))");
                }
                else
                {
                    stringBuilder.Append(
                        $"(\"{EscapeStringLiteral(comparison.Value)}\", StringComparison.OrdinalIgnoreCase)");
                }

                return true;
            default: return false;
        }
    }

    private static string EscapeStringLiteral(string? value)
    {
        if (string.IsNullOrEmpty(value)) { return string.Empty; }

        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            switch (character)
            {
                case '\\': builder.Append("\\\\"); break;
                case '"': builder.Append("\\\""); break;
                case '\r': builder.Append("\\r"); break;
                case '\n': builder.Append("\\n"); break;
                case '\t': builder.Append("\\t"); break;
                default: builder.Append(character); break;
            }
        }

        return builder.ToString();
    }

    private static string GetComparisonString(EventProperty property, ComparisonOperator op, MatchMode matchMode)
    {
        if (matchMode == MatchMode.Many)
        {
            // The Many shape ignores the operator (semantically Equals-Any-Of). Keywords gets the prefix template,
            // everything else gets the closing-paren suffix template.
            return property switch
            {
                EventProperty.Id or EventProperty.Level => $"{property}.ToString())",
                EventProperty.Keywords => $"{property}.Any",
                _ => $"{property})"
            };
        }

        return op switch
        {
            ComparisonOperator.Equals => property switch
            {
                EventProperty.Keywords => $"{property}.Any(e => string.Equals(e, ",
                EventProperty.UserId => $"{property} != null && {property}.Value == ",
                _ => $"{property} == "
            },
            ComparisonOperator.Contains => property switch
            {
                EventProperty.Id or EventProperty.ActivityId => $"{property}.ToString().Contains",
                EventProperty.Keywords => $"{property}.Any(e => e.Contains",
                EventProperty.UserId => $"{property} != null && {property}.Value.Contains",
                _ => $"{property}.Contains"
            },
            ComparisonOperator.NotEqual => property switch
            {
                EventProperty.Keywords => $"!{property}.Any(e => string.Equals(e, ",
                EventProperty.UserId => $"{property} != null && {property}.Value != ",
                _ => $"{property} != "
            },
            ComparisonOperator.NotContains => property switch
            {
                EventProperty.Id or EventProperty.ActivityId => $"!{property}.ToString().Contains",
                EventProperty.Keywords => $"!{property}.Any(e => e.Contains",
                EventProperty.UserId => $"{property} != null && !{property}.Value.Contains",
                _ => $"!{property}.Contains"
            },
            _ => string.Empty
        };
    }
}
