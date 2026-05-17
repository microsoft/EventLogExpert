// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Common;
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
            || !Lowerer.TryLower(syntax!, out var semantic, out _))
        {
            return false;
        }

        return TryDecomposeRoot(semantic!, out structured);
    }

    private static void FlattenAnd(SemanticNode node, List<SemanticNode> acc)
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

    private static void FlattenOr(SemanticNode node, List<SemanticNode> acc)
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

    private static bool TryDecomposeRoot(SemanticNode root, [NotNullWhen(true)] out BasicFilter? structured)
    {
        structured = null;

        var orClauses = new List<List<SemanticNode>>();
        var orFlat = new List<SemanticNode>();
        FlattenOr(root, orFlat);

        foreach (var orClause in orFlat)
        {
            var andFlat = new List<SemanticNode>();
            FlattenAnd(orClause, andFlat);
            orClauses.Add(andFlat);
        }

        // Map every leaf first; reject before allocating any BasicFilter parts if any leaf is unsupported.
        var mapped = new List<List<FilterComparison>>(orClauses.Count);

        foreach (var orClause in orClauses)
        {
            var conditions = new List<FilterComparison>(orClause.Count);

            foreach (var leaf in orClause)
            {
                if (!TryMapLeaf(leaf, out var comparison))
                {
                    return false;
                }

                conditions.Add(comparison);
            }

            mapped.Add(conditions);
        }

        var subFilters = ImmutableList.CreateBuilder<SubFilter>();
        var firstClause = mapped[0];
        var rootComparison = firstClause[0];

        for (var i = 1; i < firstClause.Count; i++)
        {
            subFilters.Add(new SubFilter(firstClause[i], false));
        }

        for (var c = 1; c < mapped.Count; c++)
        {
            var clause = mapped[c];
            subFilters.Add(new SubFilter(clause[0], true));

            for (var i = 1; i < clause.Count; i++)
            {
                subFilters.Add(new SubFilter(clause[i], false));
            }
        }

        structured = new BasicFilter(rootComparison, subFilters.ToImmutable());

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
            case ResolvedEventField.ComputerName:
            case ResolvedEventField.LogName:
            case ResolvedEventField.RecordId:
            case ResolvedEventField.TimeCreated:
            default:
                property = default;

                return false;
        }
    }

    private static bool TryMapLeaf(SemanticNode node, [NotNullWhen(true)] out FilterComparison? comparison)
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
                if (multi.Values.Count == 0
                    || multi.Field == ResolvedEventField.Keywords
                    || !TryMapField(multi.Field, out var multiProp))
                {
                    return false;
                }

                comparison = new FilterComparison
                {
                    Property = multiProp,
                    Operator = ComparisonOperator.Equals,
                    MatchMode = MatchMode.Many,
                    Values = multi.Values.ToImmutableList()
                };

                return true;

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
}
