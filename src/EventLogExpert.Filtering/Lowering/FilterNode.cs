// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Filtering.Lowering;

/// <summary>
///     Domain-shaped AST consumed by the binder/emitter (N2) and the decomposer (L2). Each leaf node carries enough
///     pre-resolved type information for the emitter to produce a closure that does no per-event coercion.
/// </summary>
internal abstract class FilterNode;

internal sealed class AndNode(FilterNode left, FilterNode right) : FilterNode
{
    public FilterNode Left { get; } = left;

    public FilterNode Right { get; } = right;
}

internal sealed class OrNode(FilterNode left, FilterNode right) : FilterNode
{
    public FilterNode Left { get; } = left;

    public FilterNode Right { get; } = right;
}

internal sealed class NotNode(FilterNode operand) : FilterNode
{
    public FilterNode Operand { get; } = operand;
}

/// <summary>
///     Binary comparison of <see cref="Field" /> against <see cref="Literal" /> using <see cref="Op" />. Literals are
///     coerced once at lower-time so the per-event hot path performs a raw typed compare.
/// </summary>
internal sealed class ComparisonNode(
    ResolvedEventField field,
    FilterBinaryOperator op,
    TypedLiteral literal) : FilterNode
{
    public ResolvedEventField Field { get; } = field;

    public TypedLiteral Literal { get; } = literal;

    public FilterBinaryOperator Op { get; } = op;
}

/// <summary>
///     <c>{Field}.Contains(Needle, OIC)</c> — or for Id / ActivityId the formatter shape
///     <c>{Field}.ToString().Contains(Needle, OIC)</c>. UserId resolves to the underlying SDDL string.
/// </summary>
internal sealed class ContainsNode(ResolvedEventField field, string needle, bool ignoreCase) : FilterNode
{
    public ResolvedEventField Field { get; } = field;

    public bool IgnoreCase { get; } = ignoreCase;

    public string Needle { get; } = needle;
}

/// <summary><c>Keywords.Any(e =&gt; string.Equals(e, Needle, OIC))</c>.</summary>
internal sealed class KeywordsAnyEqualsNode(string needle, bool ignoreCase) : FilterNode
{
    public bool IgnoreCase { get; } = ignoreCase;

    public string Needle { get; } = needle;
}

/// <summary><c>Keywords.Any(e =&gt; e.Contains(Needle, OIC))</c>.</summary>
internal sealed class KeywordsAnyContainsNode(string needle, bool ignoreCase) : FilterNode
{
    public bool IgnoreCase { get; } = ignoreCase;

    public string Needle { get; } = needle;
}

/// <summary><c>Keywords.Any(e =&gt; (new[] {...}).Contains(e))</c>.</summary>
internal sealed class KeywordsMatchAnyOfNode(IReadOnlyList<string> needles) : FilterNode
{
    public IReadOnlyList<string> Needles { get; } = needles;
}

/// <summary>
///     <c>(new[] {...}).Contains({Field})</c> — and the int variant <c>(new[] {...}).Contains({Field}.ToString())</c>
///     for Id and Level. <see cref="Negated" /> carries the <c>!(...)</c> ("is none of") form; the emitter keeps
///     presence-required fields (UserId) at NoMatch for an absent value regardless of negation.
/// </summary>
internal sealed class MultiEqualsNode(ResolvedEventField field, IReadOnlyList<string> values, bool negated = false)
    : FilterNode
{
    public ResolvedEventField Field { get; } = field;

    public bool Negated { get; } = negated;

    public IReadOnlyList<string> Values { get; } = values;
}

/// <summary>
///     <c>(new[] {...}).Any(e =&gt; {Field}.Contains(e, OIC))</c> — string "contains any of" over a scalar string
///     field. <see cref="Negated" /> carries the <c>!(...)</c> ("contains none of") form. <see cref="IgnoreCase" /> is
///     true for the Basic (always <c>OrdinalIgnoreCase</c>) shape. UserId is presence-required (an absent UserId is
///     NoMatch for both polarities).
/// </summary>
internal sealed class MultiContainsNode(
    ResolvedEventField field,
    IReadOnlyList<string> values,
    bool ignoreCase,
    bool negated = false) : FilterNode
{
    public ResolvedEventField Field { get; } = field;

    public bool IgnoreCase { get; } = ignoreCase;

    public bool Negated { get; } = negated;

    public IReadOnlyList<string> Values { get; } = values;
}

/// <summary>
///     <c>EventData["Name"] == v</c> / <c>!= v</c> against a dynamic named EventData field. Presence-required: the
///     emitter gates on <c>EventData.TryGetValue(FieldName, …)</c>, so an absent field never matches (positive OR
///     negative). Negation is normalized in the Lowerer by flipping <see cref="Op" /> (no separate negated flag), so the
///     decomposer maps <see cref="Op" /> directly with no ambiguity.
/// </summary>
internal sealed class EventDataComparisonNode(string fieldName, FilterBinaryOperator op, EventDataLiteral literal)
    : FilterNode
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
    : FilterNode
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
    : FilterNode
{
    public string FieldName { get; } = fieldName;

    public IReadOnlyList<EventDataLiteral> Literals { get; } = literals;
}

/// <summary>
///     <c>UserData["Event/UserData/..."] == v</c> / <c>!= v</c>: ordinal all-string equality on a structured UserData
///     path, presence-required (an absent path never matches, positive or negative). Evaluated to a tri-state so a
///     truncated field surfaces <c>Unknown</c>; negation flips <see cref="Op" /> in the Lowerer, so no NotNode wraps it.
/// </summary>
internal sealed class UserDataComparisonNode(string canonicalPath, FilterBinaryOperator op, string literal)
    : FilterNode
{
    public string CanonicalPath { get; } = canonicalPath;

    public string Literal { get; } = literal;

    public FilterBinaryOperator Op { get; } = op;
}

/// <summary>
///     <c>UserData["Event/UserData/..."].Contains(Needle, OIC)</c> on a structured UserData path, ordinal over each
///     present value. <see cref="Negated" /> is the <c>!...Contains(...)</c> (Basic <c>NotContains</c>) form;
///     presence-required.
/// </summary>
internal sealed class UserDataContainsNode(string canonicalPath, string needle, bool ignoreCase, bool negated)
    : FilterNode
{
    public string CanonicalPath { get; } = canonicalPath;

    public bool IgnoreCase { get; } = ignoreCase;

    public string Needle { get; } = needle;

    public bool Negated { get; } = negated;
}

/// <summary>
///     <c>(new[] {...}).Contains(UserData["Event/UserData/..."])</c>: any-of ordinal equality on a structured
///     UserData path, presence-required. Positive only; <c>!(any-of)</c> has no Basic form and is rejected by the Lowerer.
/// </summary>
internal sealed class UserDataMultiEqualsNode(string canonicalPath, IReadOnlyList<string> literals) : FilterNode
{
    public string CanonicalPath { get; } = canonicalPath;

    public IReadOnlyList<string> Literals { get; } = literals;
}
