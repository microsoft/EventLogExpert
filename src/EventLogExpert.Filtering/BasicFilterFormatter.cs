// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text;

namespace EventLogExpert.Filtering;

public static class BasicFilterFormatter
{
    public static bool TryFormatCondition(BasicFilterCondition condition, string? joinPrefix, out string formatted)
    {
        ArgumentNullException.ThrowIfNull(condition);

        formatted = string.Empty;

        if (condition.MatchMode == MatchMode.Single && string.IsNullOrWhiteSpace(condition.Value))
        {
            return false;
        }

        if (condition is { MatchMode: MatchMode.Many, Values.Count: <= 0 })
        {
            return false;
        }

        StringBuilder stringBuilder = new(joinPrefix ?? string.Empty);

        // The Many+non-Keywords shape is `(new[]{...}).Contains(property)` — the property-reference template
        // moves to the suffix. Everything else prepends the operator+property template.
        if (condition.MatchMode != MatchMode.Many || condition.Property is EventProperty.Keywords)
        {
            stringBuilder.Append(GetComparisonString(condition.Property, condition.Operator, condition.MatchMode));
        }

        if (condition.MatchMode == MatchMode.Many)
        {
            EmitManyValues(condition, stringBuilder);
        }
        else if (!EmitSingleValue(condition, stringBuilder))
        {
            return false;
        }

        if (condition is { MatchMode: MatchMode.Many, Property: not EventProperty.Keywords })
        {
            stringBuilder.Append(GetComparisonString(condition.Property, condition.Operator, condition.MatchMode));
        }

        formatted = stringBuilder.ToString();

        return true;
    }

    public static bool TryFormat(BasicFilter basicFilter, out string comparison) =>
        TryFormat(basicFilter, strictSubFilters: false, out comparison);

    /// <summary>
    ///     Formats <paramref name="basicFilter" /> to its Dynamic-LINQ source expression. When
    ///     <paramref name="strictSubFilters" /> is <see langword="false" /> (the default) any sub-filter whose
    ///     condition cannot be emitted is silently skipped — the lenient mode used by the runtime parse path so
    ///     that one corrupt sub-filter doesn't disqualify the whole expression. When
    ///     <paramref name="strictSubFilters" /> is <see langword="true" /> the formatter fails (returns
    ///     <see langword="false" />) the moment any sub-filter cannot be emitted — used by the editor row's
    ///     "structured matches text" gate so an incomplete sub-filter cannot silently round-trip into a
    ///     persisted <see cref="BasicFilter" /> that disagrees with the saved expression text.
    /// </summary>
    public static bool TryFormat(BasicFilter basicFilter, bool strictSubFilters, out string comparison)
    {
        ArgumentNullException.ThrowIfNull(basicFilter);

        comparison = string.Empty;

        if (!TryFormatCondition(basicFilter.Comparison, null, out var comparisonText))
        {
            return false;
        }

        StringBuilder stringBuilder = new(comparisonText);

        foreach (var subFilter in basicFilter.SubFilters)
        {
            var joinPrefix = subFilter.JoinWithAny ? " || " : " && ";

            if (TryFormatCondition(subFilter.Data, joinPrefix, out var subText))
            {
                stringBuilder.Append(subText);
            }
            else if (strictSubFilters)
            {
                return false;
            }
        }

        comparison = stringBuilder.ToString();

        return true;
    }

    private static void EmitManyValues(BasicFilterCondition condition, StringBuilder stringBuilder)
    {
        var joined = string.Join("\", \"", condition.Values.Select(EscapeStringLiteral));

        if (condition.Property is EventProperty.Keywords)
        {
            stringBuilder.Append($"(e => (new[] {{\"{joined}\"}}).Contains(e))");
        }
        else
        {
            stringBuilder.Append($"(new[] {{\"{joined}\"}}).Contains(");
        }
    }

    private static bool EmitSingleValue(BasicFilterCondition condition, StringBuilder stringBuilder)
    {
        switch (condition.Operator)
        {
            case ComparisonOperator.Equals:
            case ComparisonOperator.NotEqual:
                if (condition.Property is EventProperty.Keywords)
                {
                    stringBuilder.Append(
                        $"\"{EscapeStringLiteral(condition.Value)}\", StringComparison.OrdinalIgnoreCase))");
                }
                else
                {
                    stringBuilder.Append($"\"{EscapeStringLiteral(condition.Value)}\"");
                }

                return true;
            case ComparisonOperator.Contains:
            case ComparisonOperator.NotContains:
                if (condition.Property is EventProperty.Keywords)
                {
                    stringBuilder.Append(
                        $"(\"{EscapeStringLiteral(condition.Value)}\", StringComparison.OrdinalIgnoreCase))");
                }
                else
                {
                    stringBuilder.Append(
                        $"(\"{EscapeStringLiteral(condition.Value)}\", StringComparison.OrdinalIgnoreCase)");
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
