// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Filtering.Lowering;

/// <summary>
///     Domain-shaped AST consumed by the binder/emitter (N2) and the decomposer (L2). Each leaf node carries enough
///     pre-resolved type information for the emitter to produce a closure that does no per-event coercion.
/// </summary>
internal abstract class SemanticNode;

internal sealed class AndNode(SemanticNode left, SemanticNode right) : SemanticNode
{
    public SemanticNode Left { get; } = left;

    public SemanticNode Right { get; } = right;
}

internal sealed class OrNode(SemanticNode left, SemanticNode right) : SemanticNode
{
    public SemanticNode Left { get; } = left;

    public SemanticNode Right { get; } = right;
}

internal sealed class NotNode(SemanticNode operand) : SemanticNode
{
    public SemanticNode Operand { get; } = operand;
}

/// <summary>
///     Binary comparison of <see cref="Field" /> against <see cref="Literal" /> using <see cref="Op" />. Literals are
///     coerced once at lower-time so the per-event hot path performs a raw typed compare.
/// </summary>
internal sealed class ComparisonNode(
    ResolvedEventField field,
    FilterBinaryOperator op,
    TypedLiteral literal) : SemanticNode
{
    public ResolvedEventField Field { get; } = field;

    public TypedLiteral Literal { get; } = literal;

    public FilterBinaryOperator Op { get; } = op;
}

/// <summary>
///     <c>{Field}.Contains(Needle, OIC)</c> — or for Id / ActivityId the formatter shape
///     <c>{Field}.ToString().Contains(Needle, OIC)</c>. UserId resolves to the underlying SDDL string.
/// </summary>
internal sealed class ContainsNode(ResolvedEventField field, string needle, bool ignoreCase) : SemanticNode
{
    public ResolvedEventField Field { get; } = field;

    public bool IgnoreCase { get; } = ignoreCase;

    public string Needle { get; } = needle;
}

/// <summary><c>Keywords.Any(e =&gt; string.Equals(e, Needle, OIC))</c>.</summary>
internal sealed class KeywordsAnyEqualsNode(string needle, bool ignoreCase) : SemanticNode
{
    public bool IgnoreCase { get; } = ignoreCase;

    public string Needle { get; } = needle;
}

/// <summary><c>Keywords.Any(e =&gt; e.Contains(Needle, OIC))</c>.</summary>
internal sealed class KeywordsAnyContainsNode(string needle, bool ignoreCase) : SemanticNode
{
    public bool IgnoreCase { get; } = ignoreCase;

    public string Needle { get; } = needle;
}

/// <summary><c>Keywords.Any(e =&gt; (new[] {...}).Contains(e))</c>.</summary>
internal sealed class KeywordsMatchAnyOfNode(IReadOnlyList<string> needles) : SemanticNode
{
    public IReadOnlyList<string> Needles { get; } = needles;
}

/// <summary>
///     <c>(new[] {...}).Contains({Field})</c> — and the int variant <c>(new[] {...}).Contains({Field}.ToString())</c>
///     for Id and Level.
/// </summary>
internal sealed class MultiEqualsNode(ResolvedEventField field, IReadOnlyList<string> values) : SemanticNode
{
    public ResolvedEventField Field { get; } = field;

    public IReadOnlyList<string> Values { get; } = values;
}

/// <summary>
///     <c>EventData["Name"] == v</c> / <c>!= v</c> against a dynamic named EventData field. Presence-required: the
///     emitter gates on <c>EventData.TryGetValue(FieldName, …)</c>, so an absent field never matches (positive OR
///     negative). Negation is normalized in the Lowerer by flipping <see cref="Op" /> (no separate negated flag), so the
///     decomposer maps <see cref="Op" /> directly with no ambiguity.
/// </summary>
internal sealed class EventDataComparisonNode(string fieldName, FilterBinaryOperator op, EventDataLiteral literal)
    : SemanticNode
{
    public string FieldName { get; } = fieldName;

    public EventDataLiteral Literal { get; } = literal;

    public FilterBinaryOperator Op { get; } = op;
}

/// <summary>
///     <c>EventData["Name"].Contains(Needle, OIC)</c> against a dynamic named EventData field, string-based over
///     <see cref="EventLogExpert.Eventing.Common.Events.EventFieldValue.AsString" />. <see cref="Negated" /> carries the
///     <c>!…Contains(…)</c> form (Basic's <c>NotContains</c>); presence-required either way.
/// </summary>
internal sealed class EventDataContainsNode(string fieldName, string needle, bool ignoreCase, bool negated)
    : SemanticNode
{
    public string FieldName { get; } = fieldName;

    public bool IgnoreCase { get; } = ignoreCase;

    public string Needle { get; } = needle;

    public bool Negated { get; } = negated;
}

/// <summary>
///     <c>(new[] {...}).Contains(EventData["Name"])</c> — any-of typed value equality against a dynamic named
///     EventData field. Presence-required. Positive only: <c>!(any-of)</c> ("none-of") has no Basic representation and is
///     rejected by the Lowerer.
/// </summary>
internal sealed class EventDataMultiEqualsNode(string fieldName, IReadOnlyList<EventDataLiteral> literals)
    : SemanticNode
{
    public string FieldName { get; } = fieldName;

    public IReadOnlyList<EventDataLiteral> Literals { get; } = literals;
}
