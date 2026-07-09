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
        FilterNode root,
        [NotNullWhen(true)] out CompiledFilter? compiled,
        [NotNullWhen(false)] out string? error)
    {
        compiled = null;
        error = null;

        try
        {
            var requiresXml = FilterNodeMetadata.ContainsXmlReference(root);

            if (ContainsUserDataNode(root))
            {
                var evaluate = EmitTriState(root);

                compiled = new CompiledFilter(resolvedEvent => evaluate(resolvedEvent) == FilterMatch.Match, requiresXml)
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

    private static bool ContainsUserDataNode(FilterNode node) =>
        node switch
        {
            UserDataComparisonNode or UserDataContainsNode or UserDataMultiEqualsNode => true,
            AndNode and => ContainsUserDataNode(and.Left) || ContainsUserDataNode(and.Right),
            OrNode or => ContainsUserDataNode(or.Left) || ContainsUserDataNode(or.Right),
            NotNode not => ContainsUserDataNode(not.Operand),
            _ => false
        };

    private static Func<ResolvedEvent, bool> EmitAnd(AndNode node)
    {
        var conditions = FilterNodeMetadata.FlattenAndChain(node).Select(EmitNode).ToArray();

        // Specialize 2- and 3-condition chains: closure invocation plus inline short-circuit &&.
        return conditions.Length switch
        {
            2 => resolvedEvent => conditions[0](resolvedEvent) && conditions[1](resolvedEvent),
            3 => resolvedEvent => conditions[0](resolvedEvent) && conditions[1](resolvedEvent) && conditions[2](resolvedEvent),
            _ => resolvedEvent =>
            {
                for (var i = 0; i < conditions.Length; i++)
                {
                    if (!conditions[i](resolvedEvent)) { return false; }
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
            ResolvedEventField.Id => EmitIntComparison(static resolvedEvent => resolvedEvent.Id, node.Op, node.Literal.IntValue),
            ResolvedEventField.ProcessId => EmitNullableIntComparison(
                static resolvedEvent => resolvedEvent.ProcessId,
                node.Op,
                node.Literal.IntValue),
            ResolvedEventField.ThreadId => EmitNullableIntComparison(
                static resolvedEvent => resolvedEvent.ThreadId,
                node.Op,
                node.Literal.IntValue),
            ResolvedEventField.RecordId => EmitNullableLongComparison(
                static resolvedEvent => resolvedEvent.RecordId,
                node.Op,
                node.Literal.Kind == TypedLiteralKind.Long ? node.Literal.LongValue : node.Literal.IntValue),
            ResolvedEventField.ActivityId => EmitNullableGuidComparison(
                static resolvedEvent => resolvedEvent.ActivityId,
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
            ResolvedEventField.Id => resolvedEvent =>
            {
                Span<char> buffer = stackalloc char[11];

                return resolvedEvent.Id.TryFormat(buffer, out var written, default, CultureInfo.InvariantCulture)
                    && buffer[..written].Contains(needle, comparison);
            },
            ResolvedEventField.ProcessId => resolvedEvent =>
            {
                if (!resolvedEvent.ProcessId.HasValue) { return false; }

                Span<char> buffer = stackalloc char[11];

                return resolvedEvent.ProcessId.Value.TryFormat(buffer, out var written, default, CultureInfo.InvariantCulture)
                    && buffer[..written].Contains(needle, comparison);
            },
            ResolvedEventField.ThreadId => resolvedEvent =>
            {
                if (!resolvedEvent.ThreadId.HasValue) { return false; }

                Span<char> buffer = stackalloc char[11];

                return resolvedEvent.ThreadId.Value.TryFormat(buffer, out var written, default, CultureInfo.InvariantCulture)
                    && buffer[..written].Contains(needle, comparison);
            },
            ResolvedEventField.RecordId => resolvedEvent =>
            {
                if (!resolvedEvent.RecordId.HasValue) { return false; }

                Span<char> buffer = stackalloc char[20];

                return resolvedEvent.RecordId.Value.TryFormat(buffer, out var written, default, CultureInfo.InvariantCulture)
                    && buffer[..written].Contains(needle, comparison);
            },
            ResolvedEventField.ActivityId => resolvedEvent =>
            {
                if (!resolvedEvent.ActivityId.HasValue) { return false; }

                Span<char> buffer = stackalloc char[36];

                return resolvedEvent.ActivityId.Value.TryFormat(buffer, out var written, "D")
                    && buffer[..written].Contains(needle, comparison);
            },
            ResolvedEventField.UserId => resolvedEvent => resolvedEvent.UserId is not null && resolvedEvent.UserId.Value.Contains(needle, comparison),
            ResolvedEventField.ComputerName => resolvedEvent => resolvedEvent.ComputerName.Contains(needle, comparison),
            ResolvedEventField.Description => resolvedEvent => resolvedEvent.Description.Contains(needle, comparison),
            ResolvedEventField.Level => resolvedEvent => resolvedEvent.Level.Contains(needle, comparison),
            ResolvedEventField.LogName => resolvedEvent => resolvedEvent.LogName.Contains(needle, comparison),
            ResolvedEventField.Source => resolvedEvent => resolvedEvent.Source.Contains(needle, comparison),
            ResolvedEventField.TaskCategory => resolvedEvent => resolvedEvent.TaskCategory.Contains(needle, comparison),
            ResolvedEventField.Xml => resolvedEvent => resolvedEvent.Xml.Contains(needle, comparison),
            _ => throw new EmitException($"Cannot emit Contains for field '{node.Field}'.")
        };
    }

    private static Func<ResolvedEvent, bool> EmitDirectStringCompare(
        Func<ResolvedEvent, string> getter,
        FilterBinaryOperator op,
        string value) =>
        op switch
        {
            FilterBinaryOperator.Equal => resolvedEvent => string.Equals(getter(resolvedEvent), value, StringComparison.Ordinal),
            FilterBinaryOperator.NotEqual => resolvedEvent => !string.Equals(getter(resolvedEvent), value, StringComparison.Ordinal),
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

            return resolvedEvent =>
            {
                foreach (var field in resolvedEvent.EventData)
                {
                    if (matchesName(field.Name) && literal.MatchesValue(field.Value) == equal) { return true; }
                }

                return false;
            };
        }

        if (equal)
        {
            return resolvedEvent => resolvedEvent.EventData.TryGetValue(name, out var value) && literal.MatchesValue(value);
        }

        return resolvedEvent => resolvedEvent.EventData.TryGetValue(name, out var value) && !literal.MatchesValue(value);
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

            return resolvedEvent =>
            {
                foreach (var field in resolvedEvent.EventData)
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
            return resolvedEvent => resolvedEvent.EventData.TryGetValue(name, out var value)
                && !value.AsString().Contains(needle, comparison);
        }

        return resolvedEvent => resolvedEvent.EventData.TryGetValue(name, out var value)
            && value.AsString().Contains(needle, comparison);
    }

    private static Func<ResolvedEvent, bool> EmitEventDataMultiEquals(EventDataMultiEqualsNode node)
    {
        var name = node.FieldName;
        var literals = node.Literals;

        if (!WildcardMatcher.ContainsWildcard(name))
        {
            return resolvedEvent =>
            {
                if (!resolvedEvent.EventData.TryGetValue(name, out var value)) { return false; }

                for (var i = 0; i < literals.Count; i++)
                {
                    if (literals[i].MatchesValue(value)) { return true; }
                }

                return false;
            };
        }

        var matchesName = WildcardMatcher.Compile(name);

        return resolvedEvent =>
        {
            foreach (var field in resolvedEvent.EventData)
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

    private static Func<ResolvedEvent, bool> EmitIntComparison(
        Func<ResolvedEvent, int> getter,
        FilterBinaryOperator op,
        int value) =>
        op switch
        {
            FilterBinaryOperator.Equal => resolvedEvent => getter(resolvedEvent) == value,
            FilterBinaryOperator.NotEqual => resolvedEvent => getter(resolvedEvent) != value,
            FilterBinaryOperator.GreaterThan => resolvedEvent => getter(resolvedEvent) > value,
            FilterBinaryOperator.LessThan => resolvedEvent => getter(resolvedEvent) < value,
            FilterBinaryOperator.GreaterThanOrEqual => resolvedEvent => getter(resolvedEvent) >= value,
            FilterBinaryOperator.LessThanOrEqual => resolvedEvent => getter(resolvedEvent) <= value,
            _ => throw new EmitException($"Unsupported integer operator '{op}'.")
        };

    private static Func<ResolvedEvent, bool> EmitIntegerStringCompare(
        Func<ResolvedEvent, string> getter,
        FilterBinaryOperator op,
        string value) =>
        op switch
        {
            FilterBinaryOperator.Equal => resolvedEvent => string.Equals(getter(resolvedEvent), value, StringComparison.Ordinal),
            FilterBinaryOperator.NotEqual => resolvedEvent => !string.Equals(getter(resolvedEvent), value, StringComparison.Ordinal),
            _ => throw new EmitException($"Operator '{op}' is not supported on string-form comparison.")
        };

    private static Func<ResolvedEvent, bool> EmitKeywordsAnyContains(KeywordsAnyContainsNode node)
    {
        var needle = node.Needle;
        var comparison = node.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        return resolvedEvent => KeywordMatch.AnyContains(resolvedEvent.Keywords, needle, comparison);
    }

    private static Func<ResolvedEvent, bool> EmitKeywordsAnyEquals(KeywordsAnyEqualsNode node)
    {
        var needle = node.Needle;
        var comparison = node.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        return resolvedEvent => KeywordMatch.AnyEquals(resolvedEvent.Keywords, needle, comparison);
    }

    private static Func<ResolvedEvent, bool> EmitKeywordsMatchAnyOf(KeywordsMatchAnyOfNode node)
    {
        var needles = CompileTimeLiterals.Snapshot(node.Needles);

        return resolvedEvent => KeywordMatch.MatchAnyOf(resolvedEvent.Keywords, needles);
    }

    private static Func<ResolvedEvent, bool> EmitMultiEquals(MultiEqualsNode node) =>
        node.Field switch
        {
            ResolvedEventField.Id => EmitMultiEqualsInt(static resolvedEvent => resolvedEvent.Id, node.Values),
            ResolvedEventField.ProcessId => EmitMultiEqualsNullableInt(static resolvedEvent => resolvedEvent.ProcessId, node.Values),
            ResolvedEventField.ThreadId => EmitMultiEqualsNullableInt(static resolvedEvent => resolvedEvent.ThreadId, node.Values),
            ResolvedEventField.RecordId => EmitMultiEqualsNullableLong(static resolvedEvent => resolvedEvent.RecordId, node.Values),
            ResolvedEventField.ActivityId => EmitMultiEqualsNullableGuid(static resolvedEvent => resolvedEvent.ActivityId, node.Values),
            ResolvedEventField.UserId => EmitMultiEqualsUserId(node.Values),
            ResolvedEventField.ComputerName => EmitMultiEqualsString(static resolvedEvent => resolvedEvent.ComputerName, node.Values),
            ResolvedEventField.Description => EmitMultiEqualsString(static resolvedEvent => resolvedEvent.Description, node.Values),
            ResolvedEventField.Level => EmitMultiEqualsString(static resolvedEvent => resolvedEvent.Level, node.Values),
            ResolvedEventField.LogName => EmitMultiEqualsString(static resolvedEvent => resolvedEvent.LogName, node.Values),
            ResolvedEventField.Source => EmitMultiEqualsString(static resolvedEvent => resolvedEvent.Source, node.Values),
            ResolvedEventField.TaskCategory => EmitMultiEqualsString(static resolvedEvent => resolvedEvent.TaskCategory, node.Values),
            ResolvedEventField.Xml => EmitMultiEqualsString(static resolvedEvent => resolvedEvent.Xml, node.Values),
            _ => throw new EmitException($"Cannot emit MultiEquals for field '{node.Field}'.")
        };

    private static Func<ResolvedEvent, bool> EmitMultiEqualsInt(
        Func<ResolvedEvent, int> getter,
        IReadOnlyList<string> values)
    {
        var coerced = CompileTimeLiterals.CoerceToIntArray(values);

        return resolvedEvent =>
        {
            var actual = getter(resolvedEvent);

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

        return resolvedEvent =>
        {
            var actual = getter(resolvedEvent);

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

        return resolvedEvent =>
        {
            var actual = getter(resolvedEvent);

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

        return resolvedEvent =>
        {
            var actual = getter(resolvedEvent);

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

        return resolvedEvent =>
        {
            var actual = getter(resolvedEvent);

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

        return resolvedEvent =>
        {
            if (resolvedEvent.UserId is null) { return false; }

            var sddl = resolvedEvent.UserId.Value;

            for (var i = 0; i < snapshot.Length; i++)
            {
                if (string.Equals(sddl, snapshot[i], StringComparison.Ordinal)) { return true; }
            }

            return false;
        };
    }

    private static Func<ResolvedEvent, bool> EmitNode(FilterNode node) =>
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
            _ => throw new EmitException($"Unsupported filter node {node.GetType().Name}.")
        };

    private static Func<ResolvedEvent, bool> EmitNot(NotNode node)
    {
        if (node.Operand is ContainsNode { Field: ResolvedEventField.UserId } userIdContains)
        {
            var needle = userIdContains.Needle;

            var comparison = userIdContains.IgnoreCase
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            return resolvedEvent => resolvedEvent.UserId is not null && !resolvedEvent.UserId.Value.Contains(needle, comparison);
        }

        var inner = EmitNode(node.Operand);

        return resolvedEvent => !inner(resolvedEvent);
    }

    private static Func<ResolvedEvent, bool> EmitNullableGuidComparison(
        Func<ResolvedEvent, Guid?> getter,
        FilterBinaryOperator op,
        Guid value) =>
        op switch
        {
            FilterBinaryOperator.Equal => resolvedEvent => getter(resolvedEvent).HasValue && getter(resolvedEvent)!.Value == value,
            FilterBinaryOperator.NotEqual => resolvedEvent => !getter(resolvedEvent).HasValue || getter(resolvedEvent)!.Value != value,
            _ => throw new EmitException($"Operator '{op}' is not supported on Guid.")
        };

    private static Func<ResolvedEvent, bool> EmitNullableIntComparison(
        Func<ResolvedEvent, int?> getter,
        FilterBinaryOperator op,
        int value) =>
        op switch
        {
            FilterBinaryOperator.Equal => resolvedEvent => getter(resolvedEvent).HasValue && getter(resolvedEvent)!.Value == value,
            FilterBinaryOperator.NotEqual => resolvedEvent => !getter(resolvedEvent).HasValue || getter(resolvedEvent)!.Value != value,
            FilterBinaryOperator.GreaterThan => resolvedEvent => getter(resolvedEvent).HasValue && getter(resolvedEvent)!.Value > value,
            FilterBinaryOperator.LessThan => resolvedEvent => getter(resolvedEvent).HasValue && getter(resolvedEvent)!.Value < value,
            FilterBinaryOperator.GreaterThanOrEqual => resolvedEvent => getter(resolvedEvent).HasValue && getter(resolvedEvent)!.Value >= value,
            FilterBinaryOperator.LessThanOrEqual => resolvedEvent => getter(resolvedEvent).HasValue && getter(resolvedEvent)!.Value <= value,
            _ => throw new EmitException($"Unsupported integer operator '{op}'.")
        };

    private static Func<ResolvedEvent, bool> EmitNullableLongComparison(
        Func<ResolvedEvent, long?> getter,
        FilterBinaryOperator op,
        long value) =>
        op switch
        {
            FilterBinaryOperator.Equal => resolvedEvent => getter(resolvedEvent).HasValue && getter(resolvedEvent)!.Value == value,
            FilterBinaryOperator.NotEqual => resolvedEvent => !getter(resolvedEvent).HasValue || getter(resolvedEvent)!.Value != value,
            FilterBinaryOperator.GreaterThan => resolvedEvent => getter(resolvedEvent).HasValue && getter(resolvedEvent)!.Value > value,
            FilterBinaryOperator.LessThan => resolvedEvent => getter(resolvedEvent).HasValue && getter(resolvedEvent)!.Value < value,
            FilterBinaryOperator.GreaterThanOrEqual => resolvedEvent => getter(resolvedEvent).HasValue && getter(resolvedEvent)!.Value >= value,
            FilterBinaryOperator.LessThanOrEqual => resolvedEvent => getter(resolvedEvent).HasValue && getter(resolvedEvent)!.Value <= value,
            _ => throw new EmitException($"Unsupported long operator '{op}'.")
        };

    private static Func<ResolvedEvent, bool> EmitNullableNullCheck(
        Func<ResolvedEvent, bool> hasValue,
        FilterBinaryOperator op) =>
        op switch
        {
            FilterBinaryOperator.Equal => resolvedEvent => !hasValue(resolvedEvent),
            FilterBinaryOperator.NotEqual => resolvedEvent => hasValue(resolvedEvent),
            _ => throw new EmitException($"Operator '{op}' is not supported against null.")
        };

    private static Func<ResolvedEvent, bool> EmitNullComparison(ResolvedEventField field, FilterBinaryOperator op)
    {
        switch (field)
        {
            case ResolvedEventField.ProcessId:
                return EmitNullableNullCheck(static resolvedEvent => resolvedEvent.ProcessId.HasValue, op);
            case ResolvedEventField.ThreadId:
                return EmitNullableNullCheck(static resolvedEvent => resolvedEvent.ThreadId.HasValue, op);
            case ResolvedEventField.RecordId:
                return EmitNullableNullCheck(static resolvedEvent => resolvedEvent.RecordId.HasValue, op);
            case ResolvedEventField.ActivityId:
                return EmitNullableNullCheck(static resolvedEvent => resolvedEvent.ActivityId.HasValue, op);
            case ResolvedEventField.UserId:
                return EmitNullableNullCheck(static resolvedEvent => resolvedEvent.UserId is not null, op);
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
        var conditions = FilterNodeMetadata.FlattenOrChain(node).Select(EmitNode).ToArray();

        return conditions.Length switch
        {
            2 => resolvedEvent => conditions[0](resolvedEvent) || conditions[1](resolvedEvent),
            3 => resolvedEvent => conditions[0](resolvedEvent) || conditions[1](resolvedEvent) || conditions[2](resolvedEvent),
            _ => resolvedEvent =>
            {
                for (var i = 0; i < conditions.Length; i++)
                {
                    if (conditions[i](resolvedEvent)) { return true; }
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
            ResolvedEventField.ComputerName => EmitDirectStringCompare(static resolvedEvent => resolvedEvent.ComputerName, op, value),
            ResolvedEventField.Description => EmitDirectStringCompare(static resolvedEvent => resolvedEvent.Description, op, value),
            ResolvedEventField.Level => EmitDirectStringCompare(static resolvedEvent => resolvedEvent.Level, op, value),
            ResolvedEventField.LogName => EmitDirectStringCompare(static resolvedEvent => resolvedEvent.LogName, op, value),
            ResolvedEventField.Source => EmitDirectStringCompare(static resolvedEvent => resolvedEvent.Source, op, value),
            ResolvedEventField.TaskCategory => EmitDirectStringCompare(static resolvedEvent => resolvedEvent.TaskCategory, op, value),
            ResolvedEventField.Xml => EmitDirectStringCompare(static resolvedEvent => resolvedEvent.Xml, op, value),
            ResolvedEventField.UserId => EmitUserIdStringCompare(op, value),
            ResolvedEventField.Id => EmitIntegerStringCompare(
                static resolvedEvent => resolvedEvent.Id.ToString(CultureInfo.InvariantCulture),
                op,
                value),
            ResolvedEventField.ProcessId => EmitIntegerStringCompare(
                static resolvedEvent => resolvedEvent.ProcessId.HasValue ? resolvedEvent.ProcessId.Value.ToString(CultureInfo.InvariantCulture) :
                    string.Empty,
                op,
                value),
            ResolvedEventField.ThreadId => EmitIntegerStringCompare(
                static resolvedEvent => resolvedEvent.ThreadId.HasValue ? resolvedEvent.ThreadId.Value.ToString(CultureInfo.InvariantCulture) :
                    string.Empty,
                op,
                value),
            ResolvedEventField.RecordId => EmitIntegerStringCompare(
                static resolvedEvent => resolvedEvent.RecordId.HasValue ? resolvedEvent.RecordId.Value.ToString(CultureInfo.InvariantCulture) :
                    string.Empty,
                op,
                value),
            ResolvedEventField.ActivityId => EmitIntegerStringCompare(
                static resolvedEvent => resolvedEvent.ActivityId.HasValue ? resolvedEvent.ActivityId.Value.ToString() : string.Empty,
                op,
                value),
            ResolvedEventField.TimeCreated => throw new EmitException(
                "TimeCreated comparison against a string literal is not supported."),
            ResolvedEventField.Keywords => throw new EmitException(
                "Keywords cannot be compared directly; use Keywords.Any."),
            _ => throw new EmitException($"Unsupported field '{field}' for string comparison.")
        };

    private static Func<ResolvedEvent, FilterMatch> EmitTriState(FilterNode node)
    {
        if (!ContainsUserDataNode(node))
        {
            var boolPredicate = EmitNode(node);

            return resolvedEvent => boolPredicate(resolvedEvent) ? FilterMatch.Match : FilterMatch.NoMatch;
        }

        switch (node)
        {
            case AndNode and:
            {
                var parts = FilterNodeMetadata.FlattenAndChain(and).Select(EmitTriState).ToArray();

                return resolvedEvent => FilterMatchCombiner.And(
                    (parts, resolvedEvent),
                    parts.Length,
                    static (state, index) => state.parts[index](state.resolvedEvent));
            }
            case OrNode or:
            {
                var parts = FilterNodeMetadata.FlattenOrChain(or).Select(EmitTriState).ToArray();

                return resolvedEvent => FilterMatchCombiner.Or(
                    (parts, resolvedEvent),
                    parts.Length,
                    static (state, index) => state.parts[index](state.resolvedEvent));
            }
            case UserDataComparisonNode comparison:
                return EmitUserDataFieldMatcher(comparison.CanonicalPath, UserDataMatch.Comparison(comparison));
            case UserDataContainsNode contains:
                return EmitUserDataFieldMatcher(contains.CanonicalPath, UserDataMatch.Contains(contains));
            case UserDataMultiEqualsNode multi:
                return EmitUserDataFieldMatcher(multi.CanonicalPath, UserDataMatch.MultiEquals(multi));
            default:
                throw new EmitException($"Unsupported UserData-bearing node {node.GetType().Name}.");
        }
    }

    private static Func<ResolvedEvent, FilterMatch> EmitUserDataFieldMatcher(
        string canonicalPath,
        Func<StructuredFieldResult, FilterMatch> evaluate)
    {
        var storageKey = UserDataFieldPath.ToStorageKey(canonicalPath);

        // A '*' in the storage key marks a field-name glob (ToStorageKey has already stripped the [*] repeating-element
        // marker, so only a genuine name glob survives). Evaluate the term as if each matching stored path were its own
        // OR'd filter row.
        if (!WildcardMatcher.ContainsWildcard(storageKey))
        {
            return resolvedEvent => evaluate(resolvedEvent.TryGetUserDataValues(storageKey));
        }

        var matchesPath = WildcardMatcher.Compile(storageKey);

        return resolvedEvent => EvaluateUserDataGlob(resolvedEvent, matchesPath, evaluate);

    }

    private static Func<ResolvedEvent, bool> EmitUserIdStringCompare(FilterBinaryOperator op, string value) =>
        op switch
        {
            // Lowerer-paired null guard: bare `UserId.Value` access would NRE without it.
            FilterBinaryOperator.Equal => resolvedEvent =>
                resolvedEvent.UserId is not null && string.Equals(resolvedEvent.UserId.Value, value, StringComparison.Ordinal),
            FilterBinaryOperator.NotEqual => resolvedEvent =>
                resolvedEvent.UserId is not null && !string.Equals(resolvedEvent.UserId.Value, value, StringComparison.Ordinal),
            _ => throw new EmitException($"Operator '{op}' is not supported on UserId.")
        };

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
}
