// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Structured;
using EventLogExpert.Filtering.Evaluation;
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

            if (ContainsUserDataNode(root))
            {
                var evaluate = EmitTriState(root);

                compiled = new CompiledFilter(e => evaluate(e) == FilterMatch.Match, requiresXml)
                {
                    Evaluate = evaluate
                };

                return true;
            }

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

    private static bool ContainsUserDataNode(SemanticNode node) =>
        node switch
        {
            UserDataComparisonNode or UserDataContainsNode or UserDataMultiEqualsNode => true,
            AndNode and => ContainsUserDataNode(and.Left) || ContainsUserDataNode(and.Right),
            OrNode or => ContainsUserDataNode(or.Left) || ContainsUserDataNode(or.Right),
            NotNode not => ContainsUserDataNode(not.Operand),
            _ => false
        };

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

        // Specialize 2- and 3-condition chains: closure invocation plus inline short-circuit &&.
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

        if (node.Literal.Kind == TypedLiteralKind.String)
        {
            return EmitStringFormComparison(node.Field, node.Op, node.Literal.StringValue!);
        }

        return node.Field switch
        {
            ResolvedEventField.Id => EmitIntComparison(static e => e.Id, node.Op, node.Literal.IntValue),
            ResolvedEventField.ProcessId => EmitNullableIntComparison(
                static e => e.ProcessId,
                node.Op,
                node.Literal.IntValue),
            ResolvedEventField.ThreadId => EmitNullableIntComparison(
                static e => e.ThreadId,
                node.Op,
                node.Literal.IntValue),
            ResolvedEventField.RecordId => EmitNullableLongComparison(
                static e => e.RecordId,
                node.Op,
                node.Literal.Kind == TypedLiteralKind.Long ? node.Literal.LongValue : node.Literal.IntValue),
            ResolvedEventField.ActivityId => EmitNullableGuidComparison(
                static e => e.ActivityId,
                node.Op,
                node.Literal.GuidValue),
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
            ResolvedEventField.Id => e =>
            {
                Span<char> buffer = stackalloc char[11];

                return e.Id.TryFormat(buffer, out var written, default, CultureInfo.InvariantCulture)
                    && buffer[..written].Contains(needle, comparison);
            },
            ResolvedEventField.ProcessId => e =>
            {
                if (!e.ProcessId.HasValue) { return false; }

                Span<char> buffer = stackalloc char[11];

                return e.ProcessId.Value.TryFormat(buffer, out var written, default, CultureInfo.InvariantCulture)
                    && buffer[..written].Contains(needle, comparison);
            },
            ResolvedEventField.ThreadId => e =>
            {
                if (!e.ThreadId.HasValue) { return false; }

                Span<char> buffer = stackalloc char[11];

                return e.ThreadId.Value.TryFormat(buffer, out var written, default, CultureInfo.InvariantCulture)
                    && buffer[..written].Contains(needle, comparison);
            },
            ResolvedEventField.RecordId => e =>
            {
                if (!e.RecordId.HasValue) { return false; }

                Span<char> buffer = stackalloc char[20];

                return e.RecordId.Value.TryFormat(buffer, out var written, default, CultureInfo.InvariantCulture)
                    && buffer[..written].Contains(needle, comparison);
            },
            ResolvedEventField.ActivityId => e =>
            {
                if (!e.ActivityId.HasValue) { return false; }

                Span<char> buffer = stackalloc char[36];

                return e.ActivityId.Value.TryFormat(buffer, out var written, "D")
                    && buffer[..written].Contains(needle, comparison);
            },
            ResolvedEventField.UserId => e => e.UserId is not null && e.UserId.Value.Contains(needle, comparison),
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

    private static Func<ResolvedEvent, bool> EmitEventDataComparison(EventDataComparisonNode node)
    {
        var name = node.FieldName;
        var literal = node.Literal;
        var equal = node.Op == FilterBinaryOperator.Equal;

        if (WildcardMatcher.ContainsWildcard(name))
        {
            var matchesName = WildcardMatcher.Compile(name);

            return e =>
            {
                foreach (var field in e.EventData)
                {
                    if (matchesName(field.Name) && literal.MatchesValue(field.Value) == equal) { return true; }
                }

                return false;
            };
        }

        if (equal)
        {
            return e => e.EventData.TryGetValue(name, out var value) && literal.MatchesValue(value);
        }

        return e => e.EventData.TryGetValue(name, out var value) && !literal.MatchesValue(value);
    }

    private static Func<ResolvedEvent, bool> EmitEventDataContains(EventDataContainsNode node)
    {
        var name = node.FieldName;
        var needle = node.Needle;
        var comparison = node.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var negated = node.Negated;

        if (WildcardMatcher.ContainsWildcard(name))
        {
            var matchesName = WildcardMatcher.Compile(name);

            return e =>
            {
                foreach (var field in e.EventData)
                {
                    if (matchesName(field.Name) && field.Value.AsString().Contains(needle, comparison) != negated)
                    {
                        return true;
                    }
                }

                return false;
            };
        }

        if (negated)
        {
            return e => e.EventData.TryGetValue(name, out var value)
                && !value.AsString().Contains(needle, comparison);
        }

        return e => e.EventData.TryGetValue(name, out var value)
            && value.AsString().Contains(needle, comparison);
    }

    private static Func<ResolvedEvent, bool> EmitEventDataMultiEquals(EventDataMultiEqualsNode node)
    {
        var name = node.FieldName;
        var literals = node.Literals;

        if (WildcardMatcher.ContainsWildcard(name))
        {
            var matchesName = WildcardMatcher.Compile(name);

            return e =>
            {
                foreach (var field in e.EventData)
                {
                    if (!matchesName(field.Name)) { continue; }

                    for (var i = 0; i < literals.Count; i++)
                    {
                        if (literals[i].MatchesValue(field.Value)) { return true; }
                    }
                }

                return false;
            };
        }

        return e =>
        {
            if (!e.EventData.TryGetValue(name, out var value)) { return false; }

            for (var i = 0; i < literals.Count; i++)
            {
                if (literals[i].MatchesValue(value)) { return true; }
            }

            return false;
        };
    }

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
            EventDataComparisonNode edCmp => EmitEventDataComparison(edCmp),
            EventDataContainsNode edContains => EmitEventDataContains(edContains),
            EventDataMultiEqualsNode edMulti => EmitEventDataMultiEquals(edMulti),
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
                // ResolvedEvent string properties default to string.Empty and writer paths never assign null.
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
                static e => e.Id.ToString(CultureInfo.InvariantCulture),
                op,
                value),
            ResolvedEventField.ProcessId => EmitIntegerStringCompare(
                static e => e.ProcessId.HasValue ? e.ProcessId.Value.ToString(CultureInfo.InvariantCulture) :
                    string.Empty,
                op,
                value),
            ResolvedEventField.ThreadId => EmitIntegerStringCompare(
                static e => e.ThreadId.HasValue ? e.ThreadId.Value.ToString(CultureInfo.InvariantCulture) :
                    string.Empty,
                op,
                value),
            ResolvedEventField.RecordId => EmitIntegerStringCompare(
                static e => e.RecordId.HasValue ? e.RecordId.Value.ToString(CultureInfo.InvariantCulture) :
                    string.Empty,
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

    // A UserData-bearing filter compiles to a Kleene three-valued predicate: non-UserData subtrees (never a NotNode over
    // UserData, per LowerNegation) reuse the bool emitter lifted to Match|NoMatch, And/Or combine tri-state, and a
    // UserData term reads its stored values from ResolvedEvent.UserData by storage key.
    private static Func<ResolvedEvent, FilterMatch> EmitTriState(SemanticNode node)
    {
        if (!ContainsUserDataNode(node))
        {
            var boolPredicate = EmitNode(node);

            return e => boolPredicate(e) ? FilterMatch.Match : FilterMatch.NoMatch;
        }

        switch (node)
        {
            case AndNode and:
            {
                var parts = FlattenAndChain(and).Select(EmitTriState).ToArray();

                return e => EvaluateKleeneAnd(parts, e);
            }
            case OrNode or:
            {
                var parts = FlattenOrChain(or).Select(EmitTriState).ToArray();

                return e => EvaluateKleeneOr(parts, e);
            }
            case UserDataComparisonNode comparison:
                return EmitUserDataFieldMatcher(comparison.CanonicalPath, EmitUserDataComparison(comparison));
            case UserDataContainsNode contains:
                return EmitUserDataFieldMatcher(contains.CanonicalPath, EmitUserDataContains(contains));
            case UserDataMultiEqualsNode multi:
                return EmitUserDataFieldMatcher(multi.CanonicalPath, EmitUserDataMultiEquals(multi));
            default:
                throw new EmitException($"Unsupported UserData-bearing node {node.GetType().Name}.");
        }
    }

    private static Func<StructuredFieldResult, FilterMatch> EmitUserDataComparison(UserDataComparisonNode node)
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

    private static Func<StructuredFieldResult, FilterMatch> EmitUserDataContains(UserDataContainsNode node)
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

    private static Func<ResolvedEvent, FilterMatch> EmitUserDataFieldMatcher(
        string canonicalPath,
        Func<StructuredFieldResult, FilterMatch> evaluate)
    {
        var storageKey = UserDataFieldPath.ToStorageKey(canonicalPath);

        // A '*' in the storage key marks a field-name glob (ToStorageKey has already stripped the [*] repeating-element
        // marker, so only a genuine name glob survives). Evaluate the term as if each matching stored path were its own
        // OR'd filter row.
        if (WildcardMatcher.ContainsWildcard(storageKey))
        {
            var matchesPath = WildcardMatcher.Compile(storageKey);

            return e => EvaluateUserDataGlob(e, matchesPath, evaluate);
        }

        return e => evaluate(e.TryGetUserDataValues(storageKey));
    }

    private static Func<StructuredFieldResult, FilterMatch> EmitUserDataMultiEquals(UserDataMultiEqualsNode node)
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

    private static Func<ResolvedEvent, bool> EmitUserIdStringCompare(FilterBinaryOperator op, string value) =>
        op switch
        {
            // Lowerer-paired null guard: bare `UserId.Value` access would NRE without it.
            FilterBinaryOperator.Equal => e =>
                e.UserId is not null && string.Equals(e.UserId.Value, value, StringComparison.Ordinal),
            FilterBinaryOperator.NotEqual => e =>
                e.UserId is not null && !string.Equals(e.UserId.Value, value, StringComparison.Ordinal),
            _ => throw new EmitException($"Operator '{op}' is not supported on UserId.")
        };

    private static FilterMatch EvaluateKleeneAnd(Func<ResolvedEvent, FilterMatch>[] parts, ResolvedEvent @event)
    {
        var result = FilterMatch.Match;

        foreach (var part in parts)
        {
            var match = part(@event);

            if (match == FilterMatch.NoMatch) { return FilterMatch.NoMatch; }

            if (match == FilterMatch.Unknown) { result = FilterMatch.Unknown; }
        }

        return result;
    }

    private static FilterMatch EvaluateKleeneOr(Func<ResolvedEvent, FilterMatch>[] parts, ResolvedEvent @event)
    {
        var result = FilterMatch.NoMatch;

        foreach (var part in parts)
        {
            var match = part(@event);

            if (match == FilterMatch.Match) { return FilterMatch.Match; }

            if (match == FilterMatch.Unknown) { result = FilterMatch.Unknown; }
        }

        return result;
    }

    private static FilterMatch EvaluateUserDataGlob(
        ResolvedEvent @event,
        Func<string, bool> matchesPath,
        Func<StructuredFieldResult, FilterMatch> evaluate)
    {
        // UserData is a default array for every flat-EventData / error event; guard before iterating, mirroring
        // ResolvedEvent.TryGetUserDataValues (an absent field set is NoMatch, or Unknown when the extraction was capped).
        if (@event.UserData.IsDefaultOrEmpty)
        {
            return @event.UserDataIncomplete ? FilterMatch.Unknown : FilterMatch.NoMatch;
        }

        var result = FilterMatch.NoMatch;

        foreach (var field in @event.UserData)
        {
            if (!matchesPath(field.Path)) { continue; }

            var fieldMatch = evaluate(field.ToFieldResult(@event.UserDataIncomplete));

            if (fieldMatch == FilterMatch.Match) { return FilterMatch.Match; }

            if (fieldMatch == FilterMatch.Unknown) { result = FilterMatch.Unknown; }
        }

        // A capped field set may also have dropped a whole matching path, so a would-be decisive NoMatch (no path matched
        // the glob at all) becomes keep-visible Unknown.
        return result == FilterMatch.NoMatch && @event.UserDataIncomplete ? FilterMatch.Unknown : result;
    }

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
