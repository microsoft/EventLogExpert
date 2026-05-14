// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Lowering;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace EventLogExpert.Filtering.Emit;

internal static class Emitter
{
    public static bool TryEmit(
        SemanticNode root,
        [NotNullWhen(true)] out CompiledFilter? compiled,
        [NotNullWhen(false)] out string? error)
    {
        compiled = null;
        error = null;

        try
        {
            var requiresXml = ContainsXmlReference(root);
            var predicate = EmitNode(root);
            compiled = new CompiledFilter(predicate, requiresXml);

            return true;
        }
        catch (EmitException ex)
        {
            error = ex.Message;

            return false;
        }
    }

    private static bool ContainsXmlReference(SemanticNode node) =>
        node switch
        {
            AndNode and => ContainsXmlReference(and.Left) || ContainsXmlReference(and.Right),
            OrNode or => ContainsXmlReference(or.Left) || ContainsXmlReference(or.Right),
            NotNode not => ContainsXmlReference(not.Operand),
            ComparisonNode cmp => cmp.Field == ResolvedEventField.Xml,
            ContainsNode cn => cn.Field == ResolvedEventField.Xml,
            MultiEqualsNode mn => mn.Field == ResolvedEventField.Xml,
            _ => false
        };

    private static Func<ResolvedEvent, bool> EmitAnd(AndNode node)
    {
        var conditions = FlattenAndChain(node).Select(EmitNode).ToArray();

        // Specialize 2 / 3 condition chains: one closure invocation followed by inline short-circuit && (per N-D4).
        return conditions.Length switch
        {
            2 => e => conditions[0](e) && conditions[1](e),
            3 => e => conditions[0](e) && conditions[1](e) && conditions[2](e),
            _ => e =>
            {
                for (var i = 0; i < conditions.Length; i++)
                {
                    if (!conditions[i](e)) { return false; }
                }

                return true;
            }
        };
    }

    private static Func<ResolvedEvent, bool> EmitComparison(ComparisonNode node)
    {
        if (node.Literal.Kind == TypedLiteralKind.Null)
        {
            return EmitNullComparison(node.Field, node.Op);
        }

        // String literals route through ToString-form comparison so the formatter shape `Id == "100"` and the
        // free-text shape `Id.ToString() == "100"` both behave like Dynamic.Core: ordinal string equality on the
        // field rendered to invariant text. For pure string fields this collapses to a direct getter compare.
        if (node.Literal.Kind == TypedLiteralKind.String)
        {
            return EmitStringFormComparison(node.Field, node.Op, node.Literal.StringValue!);
        }

        return node.Field switch
        {
            ResolvedEventField.Id => EmitIntComparison(static e => e.Id, node.Op, node.Literal.IntValue),
            ResolvedEventField.ProcessId => EmitNullableIntComparison(
                static e => e.ProcessId, node.Op, node.Literal.IntValue),
            ResolvedEventField.ThreadId => EmitNullableIntComparison(
                static e => e.ThreadId, node.Op, node.Literal.IntValue),
            ResolvedEventField.RecordId => EmitNullableLongComparison(
                static e => e.RecordId,
                node.Op,
                node.Literal.Kind == TypedLiteralKind.Long ? node.Literal.LongValue : node.Literal.IntValue),
            ResolvedEventField.ActivityId => EmitNullableGuidComparison(
                static e => e.ActivityId, node.Op, node.Literal.GuidValue),
            _ => throw new EmitException(
                $"Field '{node.Field}' cannot be compared to a {node.Literal.Kind} literal.")
        };
    }

    private static Func<ResolvedEvent, bool> EmitContains(ContainsNode node)
    {
        var needle = node.Needle;
        var comparison = node.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        return node.Field switch
        {
            ResolvedEventField.Id => e => e.Id.ToString(CultureInfo.InvariantCulture).Contains(needle, comparison),
            ResolvedEventField.ActivityId => e => e.ActivityId.HasValue
                && e.ActivityId.Value.ToString().Contains(needle, comparison),
            ResolvedEventField.UserId => e => e.UserId is not null
                && e.UserId.Value.Contains(needle, comparison),
            ResolvedEventField.ComputerName => e => e.ComputerName.Contains(needle, comparison),
            ResolvedEventField.Description => e => e.Description.Contains(needle, comparison),
            ResolvedEventField.Level => e => e.Level.Contains(needle, comparison),
            ResolvedEventField.LogName => e => e.LogName.Contains(needle, comparison),
            ResolvedEventField.Source => e => e.Source.Contains(needle, comparison),
            ResolvedEventField.TaskCategory => e => e.TaskCategory.Contains(needle, comparison),
            ResolvedEventField.Xml => e => e.Xml.Contains(needle, comparison),
            _ => throw new EmitException($"Cannot emit Contains for field '{node.Field}'.")
        };
    }

    private static Func<ResolvedEvent, bool> EmitDirectStringCompare(
        Func<ResolvedEvent, string> getter,
        FilterBinaryOperator op,
        string value) =>
        op switch
        {
            FilterBinaryOperator.Equal => e => string.Equals(getter(e), value, StringComparison.Ordinal),
            FilterBinaryOperator.NotEqual => e => !string.Equals(getter(e), value, StringComparison.Ordinal),
            _ => throw new EmitException($"Operator '{op}' is not supported on string properties.")
        };

    private static Func<ResolvedEvent, bool> EmitIntComparison(
        Func<ResolvedEvent, int> getter,
        FilterBinaryOperator op,
        int value) =>
        op switch
        {
            FilterBinaryOperator.Equal => e => getter(e) == value,
            FilterBinaryOperator.NotEqual => e => getter(e) != value,
            FilterBinaryOperator.GreaterThan => e => getter(e) > value,
            FilterBinaryOperator.LessThan => e => getter(e) < value,
            FilterBinaryOperator.GreaterThanOrEqual => e => getter(e) >= value,
            FilterBinaryOperator.LessThanOrEqual => e => getter(e) <= value,
            _ => throw new EmitException($"Unsupported integer operator '{op}'.")
        };

    private static Func<ResolvedEvent, bool> EmitIntegerStringCompare(
        Func<ResolvedEvent, string> getter,
        FilterBinaryOperator op,
        string value) =>
        op switch
        {
            FilterBinaryOperator.Equal => e => string.Equals(getter(e), value, StringComparison.Ordinal),
            FilterBinaryOperator.NotEqual => e => !string.Equals(getter(e), value, StringComparison.Ordinal),
            _ => throw new EmitException($"Operator '{op}' is not supported on string-form comparison.")
        };

    private static Func<ResolvedEvent, bool> EmitKeywordsAnyContains(KeywordsAnyContainsNode node)
    {
        var needle = node.Needle;
        var comparison = node.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        return e =>
        {
            var keywords = e.Keywords;

            for (var i = 0; i < keywords.Count; i++)
            {
                if (keywords[i].Contains(needle, comparison)) { return true; }
            }

            return false;
        };
    }

    private static Func<ResolvedEvent, bool> EmitKeywordsAnyEquals(KeywordsAnyEqualsNode node)
    {
        var needle = node.Needle;
        var comparison = node.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        return e =>
        {
            var keywords = e.Keywords;

            for (var i = 0; i < keywords.Count; i++)
            {
                if (string.Equals(keywords[i], needle, comparison)) { return true; }
            }

            return false;
        };
    }

    private static Func<ResolvedEvent, bool> EmitKeywordsMatchAnyOf(KeywordsMatchAnyOfNode node)
    {
        var needles = CompileTimeLiterals.Snapshot(node.Needles);

        return e =>
        {
            var keywords = e.Keywords;

            for (var i = 0; i < keywords.Count; i++)
            {
                var keyword = keywords[i];

                for (var j = 0; j < needles.Length; j++)
                {
                    if (string.Equals(keyword, needles[j], StringComparison.Ordinal)) { return true; }
                }
            }

            return false;
        };
    }

    private static Func<ResolvedEvent, bool> EmitMultiEquals(MultiEqualsNode node) =>
        node.Field switch
        {
            ResolvedEventField.Id => EmitMultiEqualsInt(static e => e.Id, node.Values),
            ResolvedEventField.ProcessId => EmitMultiEqualsNullableInt(static e => e.ProcessId, node.Values),
            ResolvedEventField.ThreadId => EmitMultiEqualsNullableInt(static e => e.ThreadId, node.Values),
            ResolvedEventField.RecordId => EmitMultiEqualsNullableLong(static e => e.RecordId, node.Values),
            ResolvedEventField.ActivityId => EmitMultiEqualsNullableGuid(static e => e.ActivityId, node.Values),
            ResolvedEventField.UserId => EmitMultiEqualsUserId(node.Values),
            ResolvedEventField.ComputerName => EmitMultiEqualsString(static e => e.ComputerName, node.Values),
            ResolvedEventField.Description => EmitMultiEqualsString(static e => e.Description, node.Values),
            ResolvedEventField.Level => EmitMultiEqualsString(static e => e.Level, node.Values),
            ResolvedEventField.LogName => EmitMultiEqualsString(static e => e.LogName, node.Values),
            ResolvedEventField.Source => EmitMultiEqualsString(static e => e.Source, node.Values),
            ResolvedEventField.TaskCategory => EmitMultiEqualsString(static e => e.TaskCategory, node.Values),
            ResolvedEventField.Xml => EmitMultiEqualsString(static e => e.Xml, node.Values),
            _ => throw new EmitException($"Cannot emit MultiEquals for field '{node.Field}'.")
        };

    private static Func<ResolvedEvent, bool> EmitMultiEqualsInt(
        Func<ResolvedEvent, int> getter,
        IReadOnlyList<string> values)
    {
        var coerced = CompileTimeLiterals.CoerceToIntArray(values);

        return e =>
        {
            var actual = getter(e);

            for (var i = 0; i < coerced.Length; i++)
            {
                if (coerced[i] == actual) { return true; }
            }

            return false;
        };
    }

    private static Func<ResolvedEvent, bool> EmitMultiEqualsNullableGuid(
        Func<ResolvedEvent, Guid?> getter,
        IReadOnlyList<string> values)
    {
        var coerced = CompileTimeLiterals.CoerceToGuidArray(values);

        return e =>
        {
            var actual = getter(e);

            if (!actual.HasValue) { return false; }

            for (var i = 0; i < coerced.Length; i++)
            {
                if (coerced[i] == actual.Value) { return true; }
            }

            return false;
        };
    }

    private static Func<ResolvedEvent, bool> EmitMultiEqualsNullableInt(
        Func<ResolvedEvent, int?> getter,
        IReadOnlyList<string> values)
    {
        var coerced = CompileTimeLiterals.CoerceToIntArray(values);

        return e =>
        {
            var actual = getter(e);

            if (!actual.HasValue) { return false; }

            for (var i = 0; i < coerced.Length; i++)
            {
                if (coerced[i] == actual.Value) { return true; }
            }

            return false;
        };
    }

    private static Func<ResolvedEvent, bool> EmitMultiEqualsNullableLong(
        Func<ResolvedEvent, long?> getter,
        IReadOnlyList<string> values)
    {
        var coerced = CompileTimeLiterals.CoerceToLongArray(values);

        return e =>
        {
            var actual = getter(e);

            if (!actual.HasValue) { return false; }

            for (var i = 0; i < coerced.Length; i++)
            {
                if (coerced[i] == actual.Value) { return true; }
            }

            return false;
        };
    }

    private static Func<ResolvedEvent, bool> EmitMultiEqualsString(
        Func<ResolvedEvent, string> getter,
        IReadOnlyList<string> values)
    {
        var snapshot = CompileTimeLiterals.Snapshot(values);

        return e =>
        {
            var actual = getter(e);

            for (var i = 0; i < snapshot.Length; i++)
            {
                if (string.Equals(actual, snapshot[i], StringComparison.Ordinal)) { return true; }
            }

            return false;
        };
    }

    private static Func<ResolvedEvent, bool> EmitMultiEqualsUserId(IReadOnlyList<string> values)
    {
        var snapshot = CompileTimeLiterals.Snapshot(values);

        return e =>
        {
            if (e.UserId is null) { return false; }

            var sddl = e.UserId.Value;

            for (var i = 0; i < snapshot.Length; i++)
            {
                if (string.Equals(sddl, snapshot[i], StringComparison.Ordinal)) { return true; }
            }

            return false;
        };
    }

    private static Func<ResolvedEvent, bool> EmitNode(SemanticNode node) =>
        node switch
        {
            AndNode and => EmitAnd(and),
            OrNode or => EmitOr(or),
            NotNode not => EmitNot(not),
            ComparisonNode cmp => EmitComparison(cmp),
            ContainsNode cn => EmitContains(cn),
            KeywordsAnyEqualsNode kn => EmitKeywordsAnyEquals(kn),
            KeywordsAnyContainsNode kn => EmitKeywordsAnyContains(kn),
            KeywordsMatchAnyOfNode kn => EmitKeywordsMatchAnyOf(kn),
            MultiEqualsNode mn => EmitMultiEquals(mn),
            _ => throw new EmitException($"Unsupported semantic node {node.GetType().Name}.")
        };

    private static Func<ResolvedEvent, bool> EmitNot(NotNode node)
    {
        if (node.Operand is ContainsNode { Field: ResolvedEventField.UserId } userIdContains)
        {
            var needle = userIdContains.Needle;
            var comparison = userIdContains.IgnoreCase
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            return e => e.UserId is not null && !e.UserId.Value.Contains(needle, comparison);
        }

        var inner = EmitNode(node.Operand);

        return e => !inner(e);
    }

    private static Func<ResolvedEvent, bool> EmitNullableGuidComparison(
        Func<ResolvedEvent, Guid?> getter,
        FilterBinaryOperator op,
        Guid value) =>
        op switch
        {
            FilterBinaryOperator.Equal => e => getter(e).HasValue && getter(e)!.Value == value,
            FilterBinaryOperator.NotEqual => e => !getter(e).HasValue || getter(e)!.Value != value,
            _ => throw new EmitException($"Operator '{op}' is not supported on Guid.")
        };

    private static Func<ResolvedEvent, bool> EmitNullableIntComparison(
        Func<ResolvedEvent, int?> getter,
        FilterBinaryOperator op,
        int value) =>
        op switch
        {
            FilterBinaryOperator.Equal => e => getter(e).HasValue && getter(e)!.Value == value,
            FilterBinaryOperator.NotEqual => e => !getter(e).HasValue || getter(e)!.Value != value,
            FilterBinaryOperator.GreaterThan => e => getter(e).HasValue && getter(e)!.Value > value,
            FilterBinaryOperator.LessThan => e => getter(e).HasValue && getter(e)!.Value < value,
            FilterBinaryOperator.GreaterThanOrEqual => e => getter(e).HasValue && getter(e)!.Value >= value,
            FilterBinaryOperator.LessThanOrEqual => e => getter(e).HasValue && getter(e)!.Value <= value,
            _ => throw new EmitException($"Unsupported integer operator '{op}'.")
        };

    private static Func<ResolvedEvent, bool> EmitNullableLongComparison(
        Func<ResolvedEvent, long?> getter,
        FilterBinaryOperator op,
        long value) =>
        op switch
        {
            FilterBinaryOperator.Equal => e => getter(e).HasValue && getter(e)!.Value == value,
            FilterBinaryOperator.NotEqual => e => !getter(e).HasValue || getter(e)!.Value != value,
            FilterBinaryOperator.GreaterThan => e => getter(e).HasValue && getter(e)!.Value > value,
            FilterBinaryOperator.LessThan => e => getter(e).HasValue && getter(e)!.Value < value,
            FilterBinaryOperator.GreaterThanOrEqual => e => getter(e).HasValue && getter(e)!.Value >= value,
            FilterBinaryOperator.LessThanOrEqual => e => getter(e).HasValue && getter(e)!.Value <= value,
            _ => throw new EmitException($"Unsupported long operator '{op}'.")
        };

    private static Func<ResolvedEvent, bool> EmitNullableNullCheck(
        Func<ResolvedEvent, bool> hasValue,
        FilterBinaryOperator op) =>
        op switch
        {
            FilterBinaryOperator.Equal => e => !hasValue(e),
            FilterBinaryOperator.NotEqual => e => hasValue(e),
            _ => throw new EmitException($"Operator '{op}' is not supported against null.")
        };

    private static Func<ResolvedEvent, bool> EmitNullComparison(ResolvedEventField field, FilterBinaryOperator op)
    {
        switch (field)
        {
            case ResolvedEventField.ProcessId:
                return EmitNullableNullCheck(static e => e.ProcessId.HasValue, op);
            case ResolvedEventField.ThreadId:
                return EmitNullableNullCheck(static e => e.ThreadId.HasValue, op);
            case ResolvedEventField.RecordId:
                return EmitNullableNullCheck(static e => e.RecordId.HasValue, op);
            case ResolvedEventField.ActivityId:
                return EmitNullableNullCheck(static e => e.ActivityId.HasValue, op);
            case ResolvedEventField.UserId:
                return EmitNullableNullCheck(static e => e.UserId is not null, op);
            case ResolvedEventField.Id:
            case ResolvedEventField.TimeCreated:
                // Value types that can never be null. Equality with null is constant per-event.
                return op switch
                {
                    FilterBinaryOperator.Equal => static _ => false,
                    FilterBinaryOperator.NotEqual => static _ => true,
                    _ => throw new EmitException($"Operator '{op}' is not supported against null.")
                };
            case ResolvedEventField.ComputerName:
            case ResolvedEventField.Description:
            case ResolvedEventField.Level:
            case ResolvedEventField.LogName:
            case ResolvedEventField.Source:
            case ResolvedEventField.TaskCategory:
            case ResolvedEventField.Xml:
                // ResolvedEvent string properties default to string.Empty and the shipped writer paths never
                // assign null. Match Dynamic.Core's reference-compare semantics: never equal to null.
                return op switch
                {
                    FilterBinaryOperator.Equal => static _ => false,
                    FilterBinaryOperator.NotEqual => static _ => true,
                    _ => throw new EmitException($"Operator '{op}' is not supported against null.")
                };
            case ResolvedEventField.Keywords:
                throw new EmitException("Keywords cannot be compared directly; use Keywords.Any.");
            default:
                throw new EmitException($"Unsupported field '{field}' for null comparison.");
        }
    }

    private static Func<ResolvedEvent, bool> EmitOr(OrNode node)
    {
        var conditions = FlattenOrChain(node).Select(EmitNode).ToArray();

        return conditions.Length switch
        {
            2 => e => conditions[0](e) || conditions[1](e),
            3 => e => conditions[0](e) || conditions[1](e) || conditions[2](e),
            _ => e =>
            {
                for (var i = 0; i < conditions.Length; i++)
                {
                    if (conditions[i](e)) { return true; }
                }

                return false;
            }
        };
    }

    private static Func<ResolvedEvent, bool> EmitStringFormComparison(
        ResolvedEventField field,
        FilterBinaryOperator op,
        string value) =>
        field switch
        {
            ResolvedEventField.ComputerName => EmitDirectStringCompare(static e => e.ComputerName, op, value),
            ResolvedEventField.Description => EmitDirectStringCompare(static e => e.Description, op, value),
            ResolvedEventField.Level => EmitDirectStringCompare(static e => e.Level, op, value),
            ResolvedEventField.LogName => EmitDirectStringCompare(static e => e.LogName, op, value),
            ResolvedEventField.Source => EmitDirectStringCompare(static e => e.Source, op, value),
            ResolvedEventField.TaskCategory => EmitDirectStringCompare(static e => e.TaskCategory, op, value),
            ResolvedEventField.Xml => EmitDirectStringCompare(static e => e.Xml, op, value),
            ResolvedEventField.UserId => EmitUserIdStringCompare(op, value),
            ResolvedEventField.Id => EmitIntegerStringCompare(
                static e => e.Id.ToString(CultureInfo.InvariantCulture), op, value),
            ResolvedEventField.ProcessId => EmitIntegerStringCompare(
                static e => e.ProcessId.HasValue ? e.ProcessId.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
                op,
                value),
            ResolvedEventField.ThreadId => EmitIntegerStringCompare(
                static e => e.ThreadId.HasValue ? e.ThreadId.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
                op,
                value),
            ResolvedEventField.RecordId => EmitIntegerStringCompare(
                static e => e.RecordId.HasValue ? e.RecordId.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
                op,
                value),
            ResolvedEventField.ActivityId => EmitIntegerStringCompare(
                static e => e.ActivityId.HasValue ? e.ActivityId.Value.ToString() : string.Empty,
                op,
                value),
            ResolvedEventField.TimeCreated => throw new EmitException(
                "TimeCreated comparison against a string literal is not supported."),
            ResolvedEventField.Keywords => throw new EmitException(
                "Keywords cannot be compared directly; use Keywords.Any."),
            _ => throw new EmitException($"Unsupported field '{field}' for string comparison.")
        };

    private static Func<ResolvedEvent, bool> EmitUserIdStringCompare(FilterBinaryOperator op, string value) =>
        op switch
        {
            // The lowerer's UserId-guarded shape collapses `UserId != null && UserId.Value == "x"` so the runtime
            // null-check belongs to the emitter; without the guard the predicate is always false (matches
            // Dynamic.Core, which would NRE on `UserId.Value` against a null UserId).
            FilterBinaryOperator.Equal => e => e.UserId is not null
                && string.Equals(e.UserId.Value, value, StringComparison.Ordinal),
            FilterBinaryOperator.NotEqual => e => e.UserId is not null
                && !string.Equals(e.UserId.Value, value, StringComparison.Ordinal),
            _ => throw new EmitException($"Operator '{op}' is not supported on UserId.")
        };

    private static List<SemanticNode> FlattenAndChain(SemanticNode node)
    {
        var list = new List<SemanticNode>();

        Flatten(node, list);

        return list;

        static void Flatten(SemanticNode current, List<SemanticNode> accumulator)
        {
            if (current is AndNode and)
            {
                Flatten(and.Left, accumulator);
                Flatten(and.Right, accumulator);
            }
            else
            {
                accumulator.Add(current);
            }
        }
    }

    private static List<SemanticNode> FlattenOrChain(SemanticNode node)
    {
        var list = new List<SemanticNode>();

        Flatten(node, list);

        return list;

        static void Flatten(SemanticNode current, List<SemanticNode> accumulator)
        {
            if (current is OrNode or)
            {
                Flatten(or.Left, accumulator);
                Flatten(or.Right, accumulator);
            }
            else
            {
                accumulator.Add(current);
            }
        }
    }

    private sealed class EmitException(string message) : Exception(message);
}
