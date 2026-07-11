// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Structured;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Lowering;
using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.Filtering.Emit;

/// <summary>
///     The column-direct filter backend: a second emitter that reproduces every arm of <see cref="Emitter" /> by
///     reading fields column-direct through <see cref="IEventColumnReader" /> instead of typed
///     <see cref="ResolvedEvent" /> properties. It emits one unified tri-state <see cref="FilterMatch" /> per node
///     (non-UserData arms return only Match / NoMatch; UserData arms carry the full tri-state), sharing the value-level
///     cores (<see cref="UserDataMatch" />, <see cref="FilterMatchCombiner" />, <see cref="FilterNodeMetadata" />) with
///     the row emitter and the per-field null semantics with <see cref="FilterCompare" />. Keywords lists, wildcard
///     EventData field names, and glob UserData paths are read column-direct through the enumerating
///     <see cref="IEventColumnReader" /> accessors, so every row arm now has a column-direct counterpart.
/// </summary>
internal static class ColumnEmitter
{
    public static bool TryEmit(
        FilterNode root,
        [NotNullWhen(true)] out ColumnCompiledFilter? compiled,
        [NotNullWhen(false)] out string? error)
    {
        compiled = null;
        error = null;

        try
        {
            var requiresXml = FilterNodeMetadata.ContainsXmlReference(root);
            var evaluate = EmitNode(root);

            compiled = new ColumnCompiledFilter(evaluate, requiresXml);

            return true;
        }
        catch (EmitException ex)
        {
            error = ex.Message;

            return false;
        }
    }

    private static EventFieldId ContainsFieldId(ResolvedEventField field) =>
        field switch
        {
            ResolvedEventField.Id => EventFieldId.Id,
            ResolvedEventField.ProcessId => EventFieldId.ProcessId,
            ResolvedEventField.ThreadId => EventFieldId.ThreadId,
            ResolvedEventField.RecordId => EventFieldId.RecordId,
            ResolvedEventField.ActivityId => EventFieldId.ActivityId,
            ResolvedEventField.RelatedActivityId => EventFieldId.RelatedActivityId,
            ResolvedEventField.UserId => EventFieldId.UserId,
            ResolvedEventField.ComputerName => EventFieldId.ComputerName,
            ResolvedEventField.Description => EventFieldId.Description,
            ResolvedEventField.Level => EventFieldId.Level,
            ResolvedEventField.LogName => EventFieldId.LogName,
            ResolvedEventField.Source => EventFieldId.Source,
            ResolvedEventField.TaskCategory => EventFieldId.TaskCategory,
            ResolvedEventField.Opcode => EventFieldId.Opcode,
            ResolvedEventField.Xml => EventFieldId.Xml,
            _ => throw new EmitException($"Cannot emit Contains for field '{field}'.")
        };

    private static Func<IEventColumnReader, EventLocator, FilterMatch> EmitAnd(AndNode node)
    {
        var parts = FilterNodeMetadata.FlattenAndChain(node).Select(EmitNode).ToArray();

        return (reader, locator) => FilterMatchCombiner.And((parts, reader, locator), parts.Length, EvaluatePart);
    }

    private static Func<IEventColumnReader, EventLocator, FilterMatch> EmitComparison(ComparisonNode node)
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
            ResolvedEventField.Id => FilterCompare.Int64(EventFieldId.Id, node.Op, node.Literal.IntValue),
            ResolvedEventField.ProcessId => FilterCompare.NullableInt64(EventFieldId.ProcessId, node.Op, node.Literal.IntValue),
            ResolvedEventField.ThreadId => FilterCompare.NullableInt64(EventFieldId.ThreadId, node.Op, node.Literal.IntValue),
            ResolvedEventField.RecordId => FilterCompare.NullableInt64(
                EventFieldId.RecordId,
                node.Op,
                node.Literal.Kind == TypedLiteralKind.Long ? node.Literal.LongValue : node.Literal.IntValue),
            ResolvedEventField.ActivityId => FilterCompare.NullableGuid(EventFieldId.ActivityId, node.Op, node.Literal.GuidValue),
            ResolvedEventField.RelatedActivityId => FilterCompare.NullableGuid(EventFieldId.RelatedActivityId, node.Op, node.Literal.GuidValue),
            _ => throw new EmitException(
                $"Field '{node.Field}' cannot be compared to a {node.Literal.Kind} literal.")
        };
    }

    private static Func<IEventColumnReader, EventLocator, FilterMatch> EmitConstantNullComparison(FilterBinaryOperator op) =>
        op switch
        {
            FilterBinaryOperator.Equal => static (_, _) => FilterMatch.NoMatch,
            FilterBinaryOperator.NotEqual => static (_, _) => FilterMatch.Match,
            _ => throw new EmitException($"Operator '{op}' is not supported against null.")
        };

    private static Func<IEventColumnReader, EventLocator, FilterMatch> EmitContains(ContainsNode node)
    {
        var needle = node.Needle;
        var comparison = node.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var field = ContainsFieldId(node.Field);

        return (reader, locator) =>
        {
            var value = reader.GetField(locator, field);

            if (value.Kind == EventFieldValueKind.Null) { return FilterMatch.NoMatch; }

            return value.AsString().Contains(needle, comparison) ? FilterMatch.Match : FilterMatch.NoMatch;
        };
    }

    private static Func<IEventColumnReader, EventLocator, FilterMatch> EmitEventDataComparison(EventDataComparisonNode node)
    {
        var name = node.FieldName;
        var literal = node.Literal;
        var equal = node.Op == FilterBinaryOperator.Equal;

        if (!WildcardMatcher.ContainsWildcard(name))
        {
            // Presence-required: an absent named field never matches (positive OR negative).
            return (reader, locator) =>
            {
                if (!reader.TryGetEventData(locator, name, out var value)) { return FilterMatch.NoMatch; }

                return literal.MatchesValue(value) == equal ? FilterMatch.Match : FilterMatch.NoMatch;
            };
        }

        var matchesName = WildcardMatcher.Compile(name);

        // Positional enumeration: every field (including duplicate names) is tested, so a non-first duplicate can
        // satisfy the glob. Mirrors Emitter.EmitEventDataComparison's wildcard branch.
        return (reader, locator) =>
        {
            foreach (var field in reader.EnumerateEventData(locator))
            {
                if (matchesName(field.Name) && literal.MatchesValue(field.Value) == equal) { return FilterMatch.Match; }
            }

            return FilterMatch.NoMatch;
        };

    }

    private static Func<IEventColumnReader, EventLocator, FilterMatch> EmitEventDataContains(EventDataContainsNode node)
    {
        var name = node.FieldName;
        var needle = node.Needle;
        var comparison = node.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var negated = node.Negated;

        if (!WildcardMatcher.ContainsWildcard(name))
        {
            return (reader, locator) =>
            {
                if (!reader.TryGetEventData(locator, name, out var value)) { return FilterMatch.NoMatch; }

                return value.AsString().Contains(needle, comparison) != negated ?
                    FilterMatch.Match :
                    FilterMatch.NoMatch;
            };
        }

        var matchesName = WildcardMatcher.Compile(name);

        return (reader, locator) =>
        {
            foreach (var field in reader.EnumerateEventData(locator))
            {
                if (matchesName(field.Name) && field.Value.AsString().Contains(needle, comparison) != negated)
                {
                    return FilterMatch.Match;
                }
            }

            return FilterMatch.NoMatch;
        };

    }

    private static Func<IEventColumnReader, EventLocator, FilterMatch> EmitEventDataMultiEquals(EventDataMultiEqualsNode node)
    {
        var name = node.FieldName;
        var literals = node.Literals;

        if (!WildcardMatcher.ContainsWildcard(name))
        {
            return (reader, locator) =>
            {
                if (!reader.TryGetEventData(locator, name, out var value)) { return FilterMatch.NoMatch; }

                for (var i = 0; i < literals.Count; i++)
                {
                    if (literals[i].MatchesValue(value)) { return FilterMatch.Match; }
                }

                return FilterMatch.NoMatch;
            };
        }

        var matchesName = WildcardMatcher.Compile(name);

        return (reader, locator) =>
        {
            foreach (var field in reader.EnumerateEventData(locator))
            {
                if (!matchesName(field.Name)) { continue; }

                for (var i = 0; i < literals.Count; i++)
                {
                    if (literals[i].MatchesValue(field.Value)) { return FilterMatch.Match; }
                }
            }

            return FilterMatch.NoMatch;
        };

    }

    private static Func<IEventColumnReader, EventLocator, FilterMatch> EmitKeywordsAnyContains(KeywordsAnyContainsNode node)
    {
        var needle = node.Needle;
        var comparison = node.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        return (reader, locator) =>
            KeywordMatch.AnyContains(reader.GetKeywords(locator), needle, comparison) ? FilterMatch.Match : FilterMatch.NoMatch;
    }

    private static Func<IEventColumnReader, EventLocator, FilterMatch> EmitKeywordsAnyEquals(KeywordsAnyEqualsNode node)
    {
        var needle = node.Needle;
        var comparison = node.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        return (reader, locator) =>
            KeywordMatch.AnyEquals(reader.GetKeywords(locator), needle, comparison) ? FilterMatch.Match : FilterMatch.NoMatch;
    }

    private static Func<IEventColumnReader, EventLocator, FilterMatch> EmitKeywordsMatchAnyOf(KeywordsMatchAnyOfNode node)
    {
        var needles = CompileTimeLiterals.Snapshot(node.Needles);

        return (reader, locator) =>
            KeywordMatch.MatchAnyOf(reader.GetKeywords(locator), needles) ? FilterMatch.Match : FilterMatch.NoMatch;
    }

    private static Func<IEventColumnReader, EventLocator, FilterMatch> EmitMultiEquals(MultiEqualsNode node) =>
        node.Field switch
        {
            ResolvedEventField.Id => EmitMultiEqualsInt(EventFieldId.Id, node.Values),
            ResolvedEventField.ProcessId => EmitMultiEqualsInt(EventFieldId.ProcessId, node.Values),
            ResolvedEventField.ThreadId => EmitMultiEqualsInt(EventFieldId.ThreadId, node.Values),
            ResolvedEventField.RecordId => EmitMultiEqualsLong(EventFieldId.RecordId, node.Values),
            ResolvedEventField.ActivityId => EmitMultiEqualsGuid(EventFieldId.ActivityId, node.Values),
            ResolvedEventField.RelatedActivityId => EmitMultiEqualsGuid(EventFieldId.RelatedActivityId, node.Values),
            ResolvedEventField.UserId => EmitMultiEqualsUserId(node.Values),
            ResolvedEventField.ComputerName => EmitMultiEqualsString(EventFieldId.ComputerName, node.Values),
            ResolvedEventField.Description => EmitMultiEqualsString(EventFieldId.Description, node.Values),
            ResolvedEventField.Level => EmitMultiEqualsString(EventFieldId.Level, node.Values),
            ResolvedEventField.LogName => EmitMultiEqualsString(EventFieldId.LogName, node.Values),
            ResolvedEventField.Source => EmitMultiEqualsString(EventFieldId.Source, node.Values),
            ResolvedEventField.TaskCategory => EmitMultiEqualsString(EventFieldId.TaskCategory, node.Values),
            ResolvedEventField.Opcode => EmitMultiEqualsString(EventFieldId.Opcode, node.Values),
            ResolvedEventField.Xml => EmitMultiEqualsString(EventFieldId.Xml, node.Values),
            _ => throw new EmitException($"Cannot emit MultiEquals for field '{node.Field}'.")
        };

    private static Func<IEventColumnReader, EventLocator, FilterMatch> EmitMultiEqualsGuid(
        EventFieldId field,
        IReadOnlyList<string> values)
    {
        var coerced = CompileTimeLiterals.CoerceToGuidArray(values);

        return (reader, locator) =>
        {
            var value = reader.GetField(locator, field);

            if (value.Kind == EventFieldValueKind.Null) { return FilterMatch.NoMatch; }

            value.TryGetGuid(out var actual);

            for (var i = 0; i < coerced.Length; i++)
            {
                if (coerced[i] == actual) { return FilterMatch.Match; }
            }

            return FilterMatch.NoMatch;
        };
    }

    private static Func<IEventColumnReader, EventLocator, FilterMatch> EmitMultiEqualsInt(
        EventFieldId field,
        IReadOnlyList<string> values)
    {
        var coerced = CompileTimeLiterals.CoerceToIntArray(values);

        // Id is non-nullable (Null kind never occurs); a nullable id that is absent is a decisive NoMatch (the
        // nullable HasValue guard).
        return (reader, locator) =>
        {
            var value = reader.GetField(locator, field);

            if (value.Kind == EventFieldValueKind.Null) { return FilterMatch.NoMatch; }

            value.TryGetInt64(out var actual);

            for (var i = 0; i < coerced.Length; i++)
            {
                if (coerced[i] == actual) { return FilterMatch.Match; }
            }

            return FilterMatch.NoMatch;
        };
    }

    private static Func<IEventColumnReader, EventLocator, FilterMatch> EmitMultiEqualsLong(
        EventFieldId field,
        IReadOnlyList<string> values)
    {
        var coerced = CompileTimeLiterals.CoerceToLongArray(values);

        return (reader, locator) =>
        {
            var value = reader.GetField(locator, field);

            if (value.Kind == EventFieldValueKind.Null) { return FilterMatch.NoMatch; }

            value.TryGetInt64(out var actual);

            for (var i = 0; i < coerced.Length; i++)
            {
                if (coerced[i] == actual) { return FilterMatch.Match; }
            }

            return FilterMatch.NoMatch;
        };
    }

    private static Func<IEventColumnReader, EventLocator, FilterMatch> EmitMultiEqualsString(
        EventFieldId field,
        IReadOnlyList<string> values)
    {
        var snapshot = CompileTimeLiterals.Snapshot(values);

        return (reader, locator) =>
        {
            var actual = reader.GetField(locator, field).AsString();

            for (var i = 0; i < snapshot.Length; i++)
            {
                if (string.Equals(actual, snapshot[i], StringComparison.Ordinal)) { return FilterMatch.Match; }
            }

            return FilterMatch.NoMatch;
        };
    }

    private static Func<IEventColumnReader, EventLocator, FilterMatch> EmitMultiEqualsUserId(IReadOnlyList<string> values)
    {
        var snapshot = CompileTimeLiterals.Snapshot(values);

        // Presence-required: an absent UserId is a decisive NoMatch.
        return (reader, locator) =>
        {
            var value = reader.GetField(locator, EventFieldId.UserId);

            if (value.Kind == EventFieldValueKind.Null) { return FilterMatch.NoMatch; }

            var sddl = value.AsString();

            for (var i = 0; i < snapshot.Length; i++)
            {
                if (string.Equals(sddl, snapshot[i], StringComparison.Ordinal)) { return FilterMatch.Match; }
            }

            return FilterMatch.NoMatch;
        };
    }

    private static Func<IEventColumnReader, EventLocator, FilterMatch> EmitNode(FilterNode node) =>
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
            KeywordsAnyEqualsNode kae => EmitKeywordsAnyEquals(kae),
            KeywordsAnyContainsNode kac => EmitKeywordsAnyContains(kac),
            KeywordsMatchAnyOfNode kma => EmitKeywordsMatchAnyOf(kma),
            MultiEqualsNode mn => EmitMultiEquals(mn),
            UserDataComparisonNode ud => EmitUserData(ud.CanonicalPath, UserDataMatch.Comparison(ud)),
            UserDataContainsNode ud => EmitUserData(ud.CanonicalPath, UserDataMatch.Contains(ud)),
            UserDataMultiEqualsNode ud => EmitUserData(ud.CanonicalPath, UserDataMatch.MultiEquals(ud)),
            _ => throw new EmitException($"Unsupported filter node {node.GetType().Name}.")
        };

    private static Func<IEventColumnReader, EventLocator, FilterMatch> EmitNot(NotNode node)
    {
        // SPECIAL: NOT(UserId.Contains) is presence-required - an absent UserId is NoMatch, NOT the
        // naive !inner. Mirrors Emitter.EmitNot.
        if (node.Operand is ContainsNode { Field: ResolvedEventField.UserId } userIdContains)
        {
            var needle = userIdContains.Needle;

            var comparison = userIdContains.IgnoreCase
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            return (reader, locator) =>
            {
                var value = reader.GetField(locator, EventFieldId.UserId);

                if (value.Kind == EventFieldValueKind.Null) { return FilterMatch.NoMatch; }

                return value.AsString().Contains(needle, comparison) ? FilterMatch.NoMatch : FilterMatch.Match;
            };
        }

        // General: !inner lifted. NotNode never wraps a UserData term (per LowerNegation), so inner is always decisive.
        var inner = EmitNode(node.Operand);

        return (reader, locator) => Negate(inner(reader, locator));
    }

    private static Func<IEventColumnReader, EventLocator, FilterMatch> EmitNullableNullCheck(
        EventFieldId field,
        FilterBinaryOperator op) =>
        op switch
        {
            // == null is true (Match) when the field is absent; != null is true when present.
            FilterBinaryOperator.Equal => (reader, locator) =>
                reader.GetField(locator, field).Kind == EventFieldValueKind.Null
                    ? FilterMatch.Match
                    : FilterMatch.NoMatch,
            FilterBinaryOperator.NotEqual => (reader, locator) =>
                reader.GetField(locator, field).Kind == EventFieldValueKind.Null
                    ? FilterMatch.NoMatch
                    : FilterMatch.Match,
            _ => throw new EmitException($"Operator '{op}' is not supported against null.")
        };

    private static Func<IEventColumnReader, EventLocator, FilterMatch> EmitNullComparison(
        ResolvedEventField field,
        FilterBinaryOperator op)
    {
        switch (field)
        {
            case ResolvedEventField.ProcessId:
                return EmitNullableNullCheck(EventFieldId.ProcessId, op);
            case ResolvedEventField.ThreadId:
                return EmitNullableNullCheck(EventFieldId.ThreadId, op);
            case ResolvedEventField.RecordId:
                return EmitNullableNullCheck(EventFieldId.RecordId, op);
            case ResolvedEventField.ActivityId:
                return EmitNullableNullCheck(EventFieldId.ActivityId, op);
            case ResolvedEventField.RelatedActivityId:
                return EmitNullableNullCheck(EventFieldId.RelatedActivityId, op);
            case ResolvedEventField.UserId:
                return EmitNullableNullCheck(EventFieldId.UserId, op);
            case ResolvedEventField.Id:
            case ResolvedEventField.TimeCreated:
                return EmitConstantNullComparison(op);
            case ResolvedEventField.ComputerName:
            case ResolvedEventField.Description:
            case ResolvedEventField.Level:
            case ResolvedEventField.LogName:
            case ResolvedEventField.Source:
            case ResolvedEventField.TaskCategory:
            case ResolvedEventField.Opcode:
            case ResolvedEventField.Xml:
                // String properties default to string.Empty and are never null; the row emits a constant without
                // reading the field, so the column does the same.
                return EmitConstantNullComparison(op);
            case ResolvedEventField.Keywords:
                throw new EmitException("Keywords cannot be compared directly; use Keywords.Any.");
            default:
                throw new EmitException($"Unsupported field '{field}' for null comparison.");
        }
    }

    private static Func<IEventColumnReader, EventLocator, FilterMatch> EmitOr(OrNode node)
    {
        var parts = FilterNodeMetadata.FlattenOrChain(node).Select(EmitNode).ToArray();

        return (reader, locator) => FilterMatchCombiner.Or((parts, reader, locator), parts.Length, EvaluatePart);
    }

    private static Func<IEventColumnReader, EventLocator, FilterMatch> EmitStringFormComparison(
        ResolvedEventField field,
        FilterBinaryOperator op,
        string value) =>
        field switch
        {
            ResolvedEventField.ComputerName => FilterCompare.StringOrdinal(EventFieldId.ComputerName, op, value),
            ResolvedEventField.Description => FilterCompare.StringOrdinal(EventFieldId.Description, op, value),
            ResolvedEventField.Level => FilterCompare.StringOrdinal(EventFieldId.Level, op, value),
            ResolvedEventField.LogName => FilterCompare.StringOrdinal(EventFieldId.LogName, op, value),
            ResolvedEventField.Source => FilterCompare.StringOrdinal(EventFieldId.Source, op, value),
            ResolvedEventField.TaskCategory => FilterCompare.StringOrdinal(EventFieldId.TaskCategory, op, value),
            ResolvedEventField.Opcode => FilterCompare.StringOrdinal(EventFieldId.Opcode, op, value),
            ResolvedEventField.Xml => FilterCompare.StringOrdinal(EventFieldId.Xml, op, value),
            ResolvedEventField.UserId => FilterCompare.UserIdString(op, value),
            ResolvedEventField.Id => FilterCompare.StringOrdinal(EventFieldId.Id, op, value),
            ResolvedEventField.ProcessId => FilterCompare.StringOrdinal(EventFieldId.ProcessId, op, value),
            ResolvedEventField.ThreadId => FilterCompare.StringOrdinal(EventFieldId.ThreadId, op, value),
            ResolvedEventField.RecordId => FilterCompare.StringOrdinal(EventFieldId.RecordId, op, value),
            ResolvedEventField.ActivityId => FilterCompare.StringOrdinal(EventFieldId.ActivityId, op, value),
            ResolvedEventField.RelatedActivityId => FilterCompare.StringOrdinal(EventFieldId.RelatedActivityId, op, value),
            ResolvedEventField.TimeCreated => throw new EmitException(
                "TimeCreated comparison against a string literal is not supported."),
            ResolvedEventField.Keywords => throw new EmitException(
                "Keywords cannot be compared directly; use Keywords.Any."),
            _ => throw new EmitException($"Unsupported field '{field}' for string comparison.")
        };

    private static Func<IEventColumnReader, EventLocator, FilterMatch> EmitUserData(
        string canonicalPath,
        Func<StructuredFieldResult, FilterMatch> evaluate)
    {
        var storageKey = UserDataFieldPath.ToStorageKey(canonicalPath);

        // No wildcard: the exact stored path is a direct point lookup.
        if (!WildcardMatcher.ContainsWildcard(storageKey))
        {
            return (reader, locator) => evaluate(reader.GetUserData(locator, storageKey));
        }

        // A '*' surviving in the storage key is a genuine field-name glob: evaluate the term as if each matching stored
        // path were its own OR'd filter row, tri-state folded. Mirrors Emitter.EvaluateUserDataGlob (the explicit empty
        // guard is redundant with the tail, so it is omitted: an empty enumeration falls straight through to the tail).
        var matchesPath = WildcardMatcher.Compile(storageKey);

        return (reader, locator) =>
        {
            var incomplete = reader.GetUserDataIncomplete(locator);
            var result = FilterMatch.NoMatch;

            foreach (var entry in reader.EnumerateUserData(locator))
            {
                if (!matchesPath(entry.Path)) { continue; }

                var fieldMatch = evaluate(entry.Result);

                if (fieldMatch == FilterMatch.Match) { return FilterMatch.Match; }

                if (fieldMatch == FilterMatch.Unknown) { result = FilterMatch.Unknown; }
            }

            // A capped field set may also have dropped a whole matching path, so a would-be decisive NoMatch (no path
            // matched the glob at all) becomes keep-visible Unknown.
            return result == FilterMatch.NoMatch && incomplete ? FilterMatch.Unknown : result;
        };
    }

    // Evaluates part[index] against the per-event reader and locator carried in a by-value named tuple, so the And/Or
    // combine stays closure-free (a cached static method group, no per-event allocation).
    private static FilterMatch EvaluatePart(
        (Func<IEventColumnReader, EventLocator, FilterMatch>[] Parts, IEventColumnReader Reader, EventLocator Locator) state,
        int index) =>
        state.Parts[index](state.Reader, state.Locator);

    private static FilterMatch Negate(FilterMatch match) =>
        match switch
        {
            FilterMatch.Match => FilterMatch.NoMatch,
            FilterMatch.NoMatch => FilterMatch.Match,
            _ => FilterMatch.Unknown
        };
}
