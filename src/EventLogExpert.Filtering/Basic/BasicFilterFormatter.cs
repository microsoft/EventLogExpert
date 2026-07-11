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

        if (comparison.Property is EventProperty.EventData && string.IsNullOrWhiteSpace(comparison.EventDataFieldName))
        {
            return false;
        }

        if (comparison.Property is EventProperty.UserData && string.IsNullOrWhiteSpace(comparison.UserDataFieldName))
        {
            return false;
        }

        var propertyExpression = FormatPropertyExpression(comparison);

        StringBuilder stringBuilder = new(joinPrefix ?? string.Empty);

        // A non-Equals Many is valid only for the operator-aware scalar string fields; every other Many (Keywords,
        // numeric, Guid, EventData/UserData) is Equals-any only, so reject non-Equals there rather than emit text the
        // Lowerer would reject (or, for Keywords, silently drop the operator).
        if (comparison.MatchMode == MatchMode.Many
            && comparison.Operator != ComparisonOperator.Equals
            && !FilterPropertyConstraints.SupportsManyOperators(comparison.Property))
        {
            return false;
        }

        // Many + non-Keywords uses one of the four operator-aware LINQ shapes. Keywords Many and every Single shape
        // fall through to the operator+property template below.
        if (comparison is { MatchMode: MatchMode.Many, Property: not EventProperty.Keywords })
        {
            AppendManyComparison(comparison, propertyExpression, stringBuilder);
            formatted = stringBuilder.ToString();

            return true;
        }

        stringBuilder.Append(
            GetComparisonString(comparison.Property, propertyExpression, comparison.Operator, comparison.MatchMode));

        if (comparison.MatchMode == MatchMode.Many)
        {
            EmitManyValues(comparison, stringBuilder);
        }
        else if (!EmitSingleValue(comparison, stringBuilder))
        {
            return false;
        }

        formatted = stringBuilder.ToString();

        return true;
    }

    // Emits the Many + non-Keywords advanced-text shape honoring the operator:
    //   Equals       -> (new[] {"a", "b"}).Contains(F)
    //   Contains     -> (new[] {"a", "b"}).Any(e => F.Contains(e, StringComparison.OrdinalIgnoreCase))
    //   NotEqual     -> !(new[] {"a", "b"}).Contains(F)
    //   NotContains  -> !(new[] {"a", "b"}).Any(e => F.Contains(e, StringComparison.OrdinalIgnoreCase))
    private static void AppendManyComparison(
        FilterComparison comparison,
        string propertyExpression,
        StringBuilder stringBuilder)
    {
        var joined = string.Join("\", \"", comparison.Values.Select(EscapeStringLiteral));
        var negated = comparison.Operator is ComparisonOperator.NotEqual or ComparisonOperator.NotContains;
        var isContains = comparison.Operator is ComparisonOperator.Contains or ComparisonOperator.NotContains;

        if (negated) { stringBuilder.Append('!'); }

        stringBuilder.Append($"(new[] {{\"{joined}\"}})");

        stringBuilder.Append(isContains
            ? $".Any(e => {propertyExpression}.Contains(e, StringComparison.OrdinalIgnoreCase))"
            : $".Contains({propertyExpression})");
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
                else if (IsBareIntegerField(comparison.Property) && IsBareIntegerLiteral(comparison.Value))
                {
                    stringBuilder.Append(comparison.Value);
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

    // The Advanced-text reference for a comparison's property: `EventData["field"]` for an EventData row,
    // `UserData["storage-key path"]` for a UserData row, otherwise the bare property name.
    private static string FormatPropertyExpression(FilterComparison comparison) =>
        comparison.Property switch
        {
            EventProperty.EventData => $"EventData[\"{EscapeStringLiteral(comparison.EventDataFieldName)}\"]",
            EventProperty.UserData => $"UserData[\"{EscapeStringLiteral(comparison.UserDataFieldName)}\"]",
            _ => comparison.Property.ToString()
        };

    private static string GetComparisonString(
        EventProperty property,
        string propertyExpression,
        ComparisonOperator op,
        MatchMode matchMode)
    {
        if (matchMode == MatchMode.Many)
        {
            // The Many shape ignores the operator (semantically Equals-Any-Of). Keywords gets the prefix template;
            // every other field (incl. numeric — the emitter coerces the string array) gets the bare closing paren.
            return property switch
            {
                EventProperty.Keywords => $"{property}.Any",
                _ => $"{propertyExpression})"
            };
        }

        return op switch
        {
            ComparisonOperator.Equals => property switch
            {
                EventProperty.Keywords => $"{property}.Any(e => string.Equals(e, ",
                EventProperty.UserId => $"{property} != null && {property}.Value == ",
                _ => $"{propertyExpression} == "
            },
            ComparisonOperator.Contains => property switch
            {
                EventProperty.Keywords => $"{property}.Any(e => e.Contains",
                EventProperty.UserId => $"{property} != null && {property}.Value.Contains",
                _ => $"{propertyExpression}.Contains"
            },
            ComparisonOperator.NotEqual => property switch
            {
                EventProperty.Keywords => $"!{property}.Any(e => string.Equals(e, ",
                EventProperty.UserId => $"{property} != null && {property}.Value != ",
                _ => $"{propertyExpression} != "
            },
            ComparisonOperator.NotContains => property switch
            {
                EventProperty.Keywords => $"!{property}.Any(e => e.Contains",
                EventProperty.UserId => $"{property} != null && !{property}.Value.Contains",
                _ => $"!{propertyExpression}.Contains"
            },
            _ => string.Empty
        };
    }

    private static bool IsBareIntegerField(EventProperty property) =>
        property is EventProperty.Id or EventProperty.ProcessId or EventProperty.ThreadId;

    private static bool IsBareIntegerLiteral(string? value)
    {
        if (string.IsNullOrEmpty(value)) { return false; }

        foreach (var character in value)
        {
            if (!char.IsAsciiDigit(character)) { return false; }
        }

        return int.TryParse(value, out _);
    }
}
