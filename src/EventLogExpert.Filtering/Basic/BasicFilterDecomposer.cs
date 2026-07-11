// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Structured;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Filtering.Lowering;
using EventLogExpert.Filtering.Parsing;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace EventLogExpert.Filtering.Basic;

internal static class BasicFilterDecomposer
{
    public static bool TryDecompose(string? filterText, [NotNullWhen(true)] out BasicFilter? structured)
    {
        structured = null;

        if (string.IsNullOrWhiteSpace(filterText))
        {
            return false;
        }

        if (!Tokenizer.TryTokenize(filterText, out var tokens, out _)
            || !Parser.TryParse(tokens, out var syntax, out _)
            || !Lowerer.TryLower(syntax!, out var filterNode, out _))
        {
            return false;
        }

        return TryDecomposeRoot(filterNode!, out structured);
    }

    private static void FlattenAnd(FilterNode node, List<FilterNode> acc)
    {
        if (node is AndNode and)
        {
            FlattenAnd(and.Left, acc);
            FlattenAnd(and.Right, acc);
        }
        else
        {
            acc.Add(node);
        }
    }

    private static void FlattenOr(FilterNode node, List<FilterNode> acc)
    {
        if (node is OrNode or)
        {
            FlattenOr(or.Left, acc);
            FlattenOr(or.Right, acc);
        }
        else
        {
            acc.Add(node);
        }
    }

    private static bool TryDecomposeRoot(FilterNode root, [NotNullWhen(true)] out BasicFilter? structured)
    {
        structured = null;

        var orClauses = new List<List<FilterNode>>();
        var orFlat = new List<FilterNode>();
        FlattenOr(root, orFlat);

        foreach (var orClause in orFlat)
        {
            var andFlat = new List<FilterNode>();
            FlattenAnd(orClause, andFlat);
            orClauses.Add(andFlat);
        }

        // Map every leaf first; reject before allocating any BasicFilter parts if any leaf is unsupported.
        var mapped = new List<List<FilterComparison>>(orClauses.Count);

        foreach (var orClause in orClauses)
        {
            var clauseComparisons = new List<FilterComparison>(orClause.Count);

            foreach (var leaf in orClause)
            {
                if (!TryMapLeaf(leaf, out var comparison))
                {
                    return false;
                }

                clauseComparisons.Add(comparison);
            }

            mapped.Add(clauseComparisons);
        }

        var predicates = ImmutableList.CreateBuilder<FilterPredicate>();
        var firstClause = mapped[0];
        var rootComparison = firstClause[0];

        for (var i = 1; i < firstClause.Count; i++)
        {
            predicates.Add(new FilterPredicate(firstClause[i], false));
        }

        for (var c = 1; c < mapped.Count; c++)
        {
            var clause = mapped[c];
            predicates.Add(new FilterPredicate(clause[0], true));

            for (var i = 1; i < clause.Count; i++)
            {
                predicates.Add(new FilterPredicate(clause[i], false));
            }
        }

        structured = new BasicFilter(rootComparison, predicates.ToImmutable());

        return true;
    }

    private static bool TryLiteralToString(TypedLiteral literal, out string? value)
    {
        switch (literal.Kind)
        {
            case TypedLiteralKind.String:
                value = literal.StringValue;

                return value is not null;

            case TypedLiteralKind.Int:
                value = literal.IntValue.ToString(CultureInfo.InvariantCulture);

                return true;

            case TypedLiteralKind.Long:
                value = literal.LongValue.ToString(CultureInfo.InvariantCulture);

                return true;

            case TypedLiteralKind.Guid:
                value = literal.GuidValue.ToString("D", CultureInfo.InvariantCulture);

                return true;

            // BasicFilter has no encoding for null literal comparisons.
            default:
                value = null;

                return false;
        }
    }

    private static bool TryMapComparison(
        ComparisonNode cmp,
        [NotNullWhen(true)] out FilterComparison? comparison)
    {
        comparison = null;

        if (cmp.Op is not (FilterBinaryOperator.Equal or FilterBinaryOperator.NotEqual))
        {
            return false;
        }

        // Keywords as a generic ComparisonNode is not formatter vocabulary — the formatter always wraps
        // Keywords in `Keywords.Any(...)` (KeywordsAnyEquals/Contains/MatchAnyOf nodes). Reject so an
        // Advanced filter author who wrote `Keywords == "X"` doesn't get re-encoded into a different shape.
        if (cmp.Field == ResolvedEventField.Keywords)
        {
            return false;
        }

        if (!TryMapField(cmp.Field, out var property))
        {
            return false;
        }

        if (!TryLiteralToString(cmp.Literal, out var value))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var op = cmp.Op == FilterBinaryOperator.Equal
            ? ComparisonOperator.Equals
            : ComparisonOperator.NotEqual;

        comparison = new FilterComparison
        {
            Property = property,
            Operator = op,
            MatchMode = MatchMode.Single,
            Value = value
        };

        return true;
    }

    private static bool TryMapEventDataComparison(
        EventDataComparisonNode node,
        [NotNullWhen(true)] out FilterComparison? comparison)
    {
        comparison = null;

        if (string.IsNullOrWhiteSpace(node.FieldName) || string.IsNullOrWhiteSpace(node.Literal.Raw))
        {
            return false;
        }

        comparison = new FilterComparison
        {
            Property = EventProperty.EventData,
            EventDataFieldName = node.FieldName,
            Operator = node.Op == FilterBinaryOperator.Equal
                ? ComparisonOperator.Equals
                : ComparisonOperator.NotEqual,
            MatchMode = MatchMode.Single,
            Value = node.Literal.Raw
        };

        return true;
    }

    private static bool TryMapEventDataContains(
        EventDataContainsNode node,
        [NotNullWhen(true)] out FilterComparison? comparison)
    {
        comparison = null;

        // Mirror the ContainsNode guard: a case-sensitive Advanced contains is not Basic vocabulary (Basic is always
        // OrdinalIgnoreCase), so leave it Advanced-only rather than re-encode it as an OIC Basic row.
        if (!node.IgnoreCase || string.IsNullOrWhiteSpace(node.FieldName) || string.IsNullOrWhiteSpace(node.Needle))
        {
            return false;
        }

        comparison = new FilterComparison
        {
            Property = EventProperty.EventData,
            EventDataFieldName = node.FieldName,
            Operator = node.Negated ? ComparisonOperator.NotContains : ComparisonOperator.Contains,
            MatchMode = MatchMode.Single,
            Value = node.Needle
        };

        return true;
    }

    private static bool TryMapEventDataMultiEquals(
        EventDataMultiEqualsNode node,
        [NotNullWhen(true)] out FilterComparison? comparison)
    {
        comparison = null;

        if (node.Literals.Count == 0 || string.IsNullOrWhiteSpace(node.FieldName))
        {
            return false;
        }

        comparison = new FilterComparison
        {
            Property = EventProperty.EventData,
            EventDataFieldName = node.FieldName,
            Operator = ComparisonOperator.Equals,
            MatchMode = MatchMode.Many,
            Values = [.. node.Literals.Select(literal => literal.Raw)]
        };

        return true;
    }

    private static bool TryMapField(ResolvedEventField field, out EventProperty property)
    {
        switch (field)
        {
            case ResolvedEventField.Id: property = EventProperty.Id; return true;
            case ResolvedEventField.ActivityId: property = EventProperty.ActivityId; return true;
            case ResolvedEventField.Level: property = EventProperty.Level; return true;
            case ResolvedEventField.Keywords: property = EventProperty.Keywords; return true;
            case ResolvedEventField.Source: property = EventProperty.Source; return true;
            case ResolvedEventField.TaskCategory: property = EventProperty.TaskCategory; return true;
            case ResolvedEventField.ProcessId: property = EventProperty.ProcessId; return true;
            case ResolvedEventField.ThreadId: property = EventProperty.ThreadId; return true;
            case ResolvedEventField.UserId: property = EventProperty.UserId; return true;
            case ResolvedEventField.Description: property = EventProperty.Description; return true;
            case ResolvedEventField.Xml: property = EventProperty.Xml; return true;
            case ResolvedEventField.LogName: property = EventProperty.LogName; return true;
            case ResolvedEventField.Opcode: property = EventProperty.Opcode; return true;
            case ResolvedEventField.RelatedActivityId: property = EventProperty.RelatedActivityId; return true;
            case ResolvedEventField.ComputerName:
            case ResolvedEventField.RecordId:
            case ResolvedEventField.TimeCreated:
            default:
                property = default;

                return false;
        }
    }

    private static bool TryMapLeaf(FilterNode node, [NotNullWhen(true)] out FilterComparison? comparison)
    {
        comparison = null;

        switch (node)
        {
            case ComparisonNode cmp:
                return TryMapComparison(cmp, out comparison);

            case ContainsNode contains:
                if (!contains.IgnoreCase
                    || contains.Field == ResolvedEventField.Keywords
                    || !TryMapField(contains.Field, out var containsProp)
                    || string.IsNullOrWhiteSpace(contains.Needle))
                {
                    return false;
                }

                comparison = new FilterComparison
                {
                    Property = containsProp,
                    Operator = ComparisonOperator.Contains,
                    MatchMode = MatchMode.Single,
                    Value = contains.Needle
                };

                return true;

            case KeywordsAnyEqualsNode kwEq:
                if (!kwEq.IgnoreCase || string.IsNullOrWhiteSpace(kwEq.Needle))
                {
                    return false;
                }

                comparison = new FilterComparison
                {
                    Property = EventProperty.Keywords,
                    Operator = ComparisonOperator.Equals,
                    MatchMode = MatchMode.Single,
                    Value = kwEq.Needle
                };

                return true;

            case KeywordsAnyContainsNode kwContains:
                if (!kwContains.IgnoreCase || string.IsNullOrWhiteSpace(kwContains.Needle))
                {
                    return false;
                }

                comparison = new FilterComparison
                {
                    Property = EventProperty.Keywords,
                    Operator = ComparisonOperator.Contains,
                    MatchMode = MatchMode.Single,
                    Value = kwContains.Needle
                };

                return true;

            case KeywordsMatchAnyOfNode kwMany:
                if (kwMany.Needles.Count == 0)
                {
                    return false;
                }

                comparison = new FilterComparison
                {
                    Property = EventProperty.Keywords,
                    Operator = ComparisonOperator.Equals,
                    MatchMode = MatchMode.Many,
                    Values = kwMany.Needles.ToImmutableList()
                };

                return true;

            case MultiEqualsNode multi:
                // Equals-any maps for any Many-capable field; the negated "is none of" form only maps for the scalar
                // string fields the Basic editor can author (keeps the decomposer in lock-step with the UI).
                if (multi.Values.Count == 0
                    || multi.Field == ResolvedEventField.Keywords
                    || !TryMapField(multi.Field, out var multiProp)
                    || (multi.Negated && !FilterPropertyConstraints.SupportsManyOperators(multiProp)))
                {
                    return false;
                }

                comparison = new FilterComparison
                {
                    Property = multiProp,
                    Operator = multi.Negated ? ComparisonOperator.NotEqual : ComparisonOperator.Equals,
                    MatchMode = MatchMode.Many,
                    Values = multi.Values.ToImmutableList()
                };

                return true;

            case MultiContainsNode multiContains:
                // Basic is always OrdinalIgnoreCase, and contains-any / contains-none are operator-aware multi kinds
                // only the scalar string fields expose, so anything else stays Advanced-only.
                if (multiContains.Values.Count == 0
                    || !multiContains.IgnoreCase
                    || !TryMapField(multiContains.Field, out var multiContainsProp)
                    || !FilterPropertyConstraints.SupportsManyOperators(multiContainsProp))
                {
                    return false;
                }

                comparison = new FilterComparison
                {
                    Property = multiContainsProp,
                    Operator = multiContains.Negated ? ComparisonOperator.NotContains : ComparisonOperator.Contains,
                    MatchMode = MatchMode.Many,
                    Values = multiContains.Values.ToImmutableList()
                };

                return true;

            case EventDataComparisonNode eventDataComparison:
                return TryMapEventDataComparison(eventDataComparison, out comparison);

            case EventDataContainsNode eventDataContains:
                return TryMapEventDataContains(eventDataContains, out comparison);

            case EventDataMultiEqualsNode eventDataMulti:
                return TryMapEventDataMultiEquals(eventDataMulti, out comparison);

            case UserDataComparisonNode userDataComparison:
                return TryMapUserDataComparison(userDataComparison, out comparison);

            case UserDataContainsNode userDataContains:
                return TryMapUserDataContains(userDataContains, out comparison);

            case UserDataMultiEqualsNode userDataMulti:
                return TryMapUserDataMultiEquals(userDataMulti, out comparison);

            case NotNode not:
                return TryMapNegatedLeaf(not, out comparison);

            default:
                return false;
        }
    }

    private static bool TryMapNegatedLeaf(NotNode not, [NotNullWhen(true)] out FilterComparison? comparison)
    {
        comparison = null;

        switch (not.Operand)
        {
            case ContainsNode contains:
                if (!contains.IgnoreCase
                    || contains.Field == ResolvedEventField.Keywords
                    || !TryMapField(contains.Field, out var containsProp)
                    || string.IsNullOrWhiteSpace(contains.Needle))
                {
                    return false;
                }

                comparison = new FilterComparison
                {
                    Property = containsProp,
                    Operator = ComparisonOperator.NotContains,
                    MatchMode = MatchMode.Single,
                    Value = contains.Needle
                };

                return true;

            case KeywordsAnyEqualsNode kwEq:
                if (!kwEq.IgnoreCase || string.IsNullOrWhiteSpace(kwEq.Needle))
                {
                    return false;
                }

                comparison = new FilterComparison
                {
                    Property = EventProperty.Keywords,
                    Operator = ComparisonOperator.NotEqual,
                    MatchMode = MatchMode.Single,
                    Value = kwEq.Needle
                };

                return true;

            case KeywordsAnyContainsNode kwContains:
                if (!kwContains.IgnoreCase || string.IsNullOrWhiteSpace(kwContains.Needle))
                {
                    return false;
                }

                comparison = new FilterComparison
                {
                    Property = EventProperty.Keywords,
                    Operator = ComparisonOperator.NotContains,
                    MatchMode = MatchMode.Single,
                    Value = kwContains.Needle
                };

                return true;

            // Anything else under NotNode (raw ComparisonNode, double negation, KeywordsMatchAnyOfNode,
            // MultiEqualsNode, AndNode, OrNode, etc.) is not formatter vocabulary.
            default:
                return false;
        }
    }

    private static bool TryMapUserDataComparison(
        UserDataComparisonNode node,
        [NotNullWhen(true)] out FilterComparison? comparison)
    {
        comparison = null;

        if (string.IsNullOrWhiteSpace(node.CanonicalPath) || string.IsNullOrWhiteSpace(node.Literal))
        {
            return false;
        }

        comparison = new FilterComparison
        {
            Property = EventProperty.UserData,
            UserDataFieldName = UserDataFieldPath.ToStorageKey(node.CanonicalPath),
            Operator = node.Op == FilterBinaryOperator.Equal
                ? ComparisonOperator.Equals
                : ComparisonOperator.NotEqual,
            MatchMode = MatchMode.Single,
            Value = node.Literal
        };

        return true;
    }

    private static bool TryMapUserDataContains(
        UserDataContainsNode node,
        [NotNullWhen(true)] out FilterComparison? comparison)
    {
        comparison = null;

        // A case-sensitive contains is not Basic vocabulary (Basic is always OrdinalIgnoreCase); leave it Advanced-only.
        if (!node.IgnoreCase || string.IsNullOrWhiteSpace(node.CanonicalPath) || string.IsNullOrWhiteSpace(node.Needle))
        {
            return false;
        }

        comparison = new FilterComparison
        {
            Property = EventProperty.UserData,
            UserDataFieldName = UserDataFieldPath.ToStorageKey(node.CanonicalPath),
            Operator = node.Negated ? ComparisonOperator.NotContains : ComparisonOperator.Contains,
            MatchMode = MatchMode.Single,
            Value = node.Needle
        };

        return true;
    }

    private static bool TryMapUserDataMultiEquals(
        UserDataMultiEqualsNode node,
        [NotNullWhen(true)] out FilterComparison? comparison)
    {
        comparison = null;

        if (node.Literals.Count == 0 || string.IsNullOrWhiteSpace(node.CanonicalPath))
        {
            return false;
        }

        comparison = new FilterComparison
        {
            Property = EventProperty.UserData,
            UserDataFieldName = UserDataFieldPath.ToStorageKey(node.CanonicalPath),
            Operator = ComparisonOperator.Equals,
            MatchMode = MatchMode.Many,
            Values = [.. node.Literals]
        };

        return true;
    }
}
