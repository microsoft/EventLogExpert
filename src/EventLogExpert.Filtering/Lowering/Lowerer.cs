// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Parsing;
using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.Filtering.Lowering;

/// <summary>
///     Walks a <see cref="SyntaxNode" /> tree and emits a <see cref="SemanticNode" /> tree by recognizing the closed
///     vocabulary of templates that <see cref="BasicFilterFormatter" /> emits, plus the small extension surface used by
///     hand-written Advanced filters. Unknown shapes return a descriptive error rather than silently falling back.
/// </summary>
internal static class Lowerer
{
    private enum FieldExpressionShape
    {
        DirectIdentifier,
        ToStringCall
    }

    public static bool TryLower(SyntaxNode root, out SemanticNode? semantic, [NotNullWhen(false)] out string? error)
    {
        semantic = null;
        error = null;

        try
        {
            semantic = Lower(root);

            return true;
        }
        catch (LowerException ex)
        {
            error = ex.Message;

            return false;
        }
    }

    private static TypedLiteralKind ChooseLiteralKindForComparison(
        ResolvedEventField field,
        FieldExpressionShape shape)
    {
        // `Property.ToString() == "literal"` always compares as strings (normalized to a string compare).
        if (shape == FieldExpressionShape.ToStringCall) { return TypedLiteralKind.String; }

        return field switch
        {
            ResolvedEventField.Id or ResolvedEventField.ProcessId or ResolvedEventField.ThreadId =>
                TypedLiteralKind.Int,
            ResolvedEventField.RecordId => TypedLiteralKind.Long,
            ResolvedEventField.ActivityId => TypedLiteralKind.Guid,
            _ => TypedLiteralKind.String
        };
    }

    private static TypedLiteral CoerceLiteral(LiteralSyntax literal, TypedLiteralKind targetKind)
    {
        switch (literal.Kind)
        {
            case LiteralKind.Null:
                return TypedLiteral.Null;
            case LiteralKind.String:
                if (!TypedLiteral.TryCoerce(literal.Text, targetKind, out var typed))
                {
                    throw new LowerException(
                        $"Cannot coerce literal '{literal.Text}' to {targetKind} (position {literal.Position}).");
                }

                return typed;
            case LiteralKind.Int:
                if (targetKind == TypedLiteralKind.String)
                {
                    return TypedLiteral.String(literal.Text);
                }

                if (!TypedLiteral.TryCoerce(literal.Text, targetKind, out var typedInt))
                {
                    throw new LowerException(
                        $"Cannot coerce numeric literal '{literal.Text}' to {targetKind} (position {literal.Position}).");
                }

                return typedInt;
            default:
                throw new LowerException($"Unsupported literal kind '{literal.Kind}' (position {literal.Position}).");
        }
    }

    private static bool ContainsEventDataNode(SemanticNode node) =>
        node switch
        {
            EventDataComparisonNode or EventDataContainsNode or EventDataMultiEqualsNode => true,
            AndNode and => ContainsEventDataNode(and.Left) || ContainsEventDataNode(and.Right),
            OrNode or => ContainsEventDataNode(or.Left) || ContainsEventDataNode(or.Right),
            NotNode not => ContainsEventDataNode(not.Operand),
            _ => false
        };

    private static IReadOnlyList<string> ExtractStringArray(ArrayCreationSyntax array, int position)
    {
        if (array.Elements.Count == 0)
        {
            throw new LowerException($"Array literal must have at least one element (position {position}).");
        }

        var values = new List<string>(array.Elements.Count);

        foreach (var element in array.Elements)
        {
            if (element is not LiteralSyntax { Kind: LiteralKind.String } strLit)
            {
                throw new LowerException(
                    $"Array literal elements must be string literals (position {element.Position}).");
            }

            values.Add(strLit.Text);
        }

        return values;
    }

    private static void FlattenAnd(SyntaxNode node, List<SyntaxNode> acc)
    {
        if (node is BinarySyntax { Op: TokenKind.AndAnd } and)
        {
            FlattenAnd(and.Left, acc);
            FlattenAnd(and.Right, acc);
        }
        else
        {
            acc.Add(node);
        }
    }

    private static string GetEventDataLiteralText(LiteralSyntax literal) =>
        literal.Kind switch
        {
            LiteralKind.String => literal.Text,
            LiteralKind.Int => literal.Text,
            _ => throw new LowerException(
                $"EventData comparison value must be a string or integer literal (position {literal.Position}).")
        };

    private static bool IsCaseInsensitiveMatch(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool IsComparisonOp(TokenKind op) =>
        op is TokenKind.EqEq
            or TokenKind.NotEq
            or TokenKind.Lt
            or TokenKind.Gt
            or TokenKind.Le
            or TokenKind.Ge;

    private static bool IsLambdaParam(SyntaxNode node, string parameterName) =>
        node is IdentifierSyntax id && IsCaseInsensitiveMatch(id.Name, parameterName);

    private static bool IsNullCheckOnIdentifier(BinarySyntax bin, string identifier)
    {
        if (bin.Left is IdentifierSyntax leftId
            && IsCaseInsensitiveMatch(leftId.Name, identifier)
            && bin.Right is LiteralSyntax { Kind: LiteralKind.Null })
        {
            return true;
        }

        if (bin.Right is IdentifierSyntax rightId
            && IsCaseInsensitiveMatch(rightId.Name, identifier)
            && bin.Left is LiteralSyntax { Kind: LiteralKind.Null })
        {
            return true;
        }

        return false;
    }

    private static bool IsStringComparisonOIC(SyntaxNode node) =>
        node is MemberAccessSyntax mem
        && IsCaseInsensitiveMatch(mem.Name, "OrdinalIgnoreCase")
        && mem.Target is IdentifierSyntax id
        && IsCaseInsensitiveMatch(id.Name, "StringComparison");

    private static bool IsUserIdValue(MemberAccessSyntax mem) =>
        IsCaseInsensitiveMatch(mem.Name, "Value")
        && mem.Target is IdentifierSyntax id
        && IsCaseInsensitiveMatch(id.Name, "UserId");

    private static SemanticNode Lower(SyntaxNode node)
    {
        switch (node)
        {
            case BinarySyntax { Op: TokenKind.OrOr } orNode:
                return new OrNode(Lower(orNode.Left), Lower(orNode.Right));
            case BinarySyntax { Op: TokenKind.AndAnd } andNode:
                return LowerAndChain(andNode);
            case BinarySyntax bin when IsComparisonOp(bin.Op):
                return LowerComparison(bin);
            case UnarySyntax { Op: TokenKind.Bang } neg:
                return LowerNegation(neg);
            case MethodCallSyntax call:
                return LowerMethodCall(call);
            case BinarySyntax bin:
                throw new LowerException($"Operator '{bin.Op}' is not supported here (position {bin.Position}).");
            default:
                throw new LowerException($"Unsupported expression at position {node.Position}.");
        }
    }

    private static SemanticNode LowerAndChain(BinarySyntax andNode)
    {
        var flat = new List<SyntaxNode>();
        FlattenAnd(andNode, flat);

        var lowered = new List<SemanticNode>(flat.Count);
        var i = 0;

        while (i < flat.Count)
        {
            if (i + 1 < flat.Count
                && flat[i] is BinarySyntax { Op: TokenKind.NotEq } notEq
                && IsNullCheckOnIdentifier(notEq, "UserId")
                && TryLowerUserIdRhs(flat[i + 1], out var collapsed))
            {
                lowered.Add(collapsed);
                i += 2;

                continue;
            }

            lowered.Add(Lower(flat[i]));
            i++;
        }

        if (lowered.Count == 0)
        {
            // Defensive: the caller pattern-matches AndAnd so FlattenAnd produces >= 2 entries and the loop
            // above produces >= 1 lowered entry. Surfacing this as LowerException keeps TryLower's catch
            // contract intact (the file-header decomposer claim "Never throws on malformed input").
            throw new LowerException($"Internal: AND chain flattened to no lowered nodes (position {andNode.Position}).");
        }

        var result = lowered[0];

        for (var k = 1; k < lowered.Count; k++)
        {
            result = new AndNode(result, lowered[k]);
        }

        return result;
    }

    private static SemanticNode LowerComparison(BinarySyntax bin)
    {
        if (TryResolveEventDataField(bin.Left, out var eventDataFieldName))
        {
            return LowerEventDataComparison(eventDataFieldName, bin);
        }

        // Property <op> Literal  -or-  Property.ToString() <op> Literal (formatter shape, normalized away)
        var (field, fieldExpression) = ResolveFieldOrToString(bin.Left, bin.Position);

        if (bin.Right is not LiteralSyntax literal)
        {
            throw new LowerException(
                $"Right-hand side of comparison must be a literal (position {bin.Right.Position}).");
        }

        var literalKind = ChooseLiteralKindForComparison(field, fieldExpression);
        var typed = CoerceLiteral(literal, literalKind);

        return new ComparisonNode(field, MapBinaryOp(bin.Op), typed);
    }

    private static SemanticNode LowerEventDataComparison(string fieldName, BinarySyntax bin)
    {
        if (bin.Op is not (TokenKind.EqEq or TokenKind.NotEq))
        {
            throw new LowerException(
                $"Operator '{bin.Op}' is not supported on EventData fields; use '==' or '!=' (position {bin.Position}).");
        }

        if (bin.Right is not LiteralSyntax literal)
        {
            throw new LowerException(
                $"Right-hand side of an EventData comparison must be a literal (position {bin.Right.Position}).");
        }

        var op = bin.Op == TokenKind.EqEq ? FilterBinaryOperator.Equal : FilterBinaryOperator.NotEqual;

        return new EventDataComparisonNode(fieldName, op, EventDataLiteral.Parse(GetEventDataLiteralText(literal)));
    }

    private static SemanticNode LowerKeywordsAnyLambda(LambdaSyntax lambda)
    {
        var p = lambda.ParameterName;
        var body = lambda.Body;

        // string.Equals(p, "needle", StringComparison.OIC)
        if (body is MethodCallSyntax stringEquals
            && IsCaseInsensitiveMatch(stringEquals.Name, "Equals")
            && stringEquals.Target is IdentifierSyntax stringTarget
            && IsCaseInsensitiveMatch(stringTarget.Name, "string")
            && stringEquals.Arguments.Count == 3
            && IsLambdaParam(stringEquals.Arguments[0], p)
            && stringEquals.Arguments[1] is LiteralSyntax { Kind: LiteralKind.String } eqLit
            && IsStringComparisonOIC(stringEquals.Arguments[2]))
        {
            return new KeywordsAnyEqualsNode(eqLit.Text, true);
        }

        // p.Contains("needle", OIC)
        if (body is MethodCallSyntax pContains
            && IsCaseInsensitiveMatch(pContains.Name, "Contains")
            && IsLambdaParam(pContains.Target, p))
        {
            var (needle, ignoreCase) = ParseContainsArgs(pContains.Arguments, pContains.Position);

            return new KeywordsAnyContainsNode(needle, ignoreCase);
        }

        // (new[] {...}).Contains(p)
        if (body is MethodCallSyntax arrayContains
            && IsCaseInsensitiveMatch(arrayContains.Name, "Contains")
            && arrayContains is { Target: ArrayCreationSyntax arr, Arguments.Count: 1 }
            && IsLambdaParam(arrayContains.Arguments[0], p))
        {
            var elements = ExtractStringArray(arr, arrayContains.Position);

            return new KeywordsMatchAnyOfNode(elements);
        }

        throw new LowerException(
            $"Unsupported Keywords.Any lambda body (position {body.Position}).");
    }

    private static SemanticNode LowerMethodCall(MethodCallSyntax call)
    {
        // (new[] {...}).Contains(P)  -or-  (new[] {...}).Contains(P.ToString())
        if (IsCaseInsensitiveMatch(call.Name, "Contains")
            && call is { Target: ArrayCreationSyntax array, Arguments.Count: 1 })
        {
            var elements = ExtractStringArray(array, call.Position);

            // EventData any-of must be recognized before ResolveFieldOrToString, which rejects an IndexAccess argument.
            if (TryResolveEventDataField(call.Arguments[0], out var eventDataAnyOfName))
            {
                return new EventDataMultiEqualsNode(
                    eventDataAnyOfName,
                    [.. elements.Select(EventDataLiteral.Parse)]);
            }

            var argField = ResolveFieldOrToString(call.Arguments[0], call.Arguments[0].Position).Field;

            return new MultiEqualsNode(argField, elements);
        }

        // EventData["Name"].Contains(needle, OIC)
        if (IsCaseInsensitiveMatch(call.Name, "Contains")
            && TryResolveEventDataField(call.Target, out var eventDataContainsName))
        {
            var (needle, ignoreCase) = ParseContainsArgs(call.Arguments, call.Position);

            return new EventDataContainsNode(eventDataContainsName, needle, ignoreCase, negated: false);
        }

        // P.Contains(needle, StringComparison.OrdinalIgnoreCase) for any Contains-supported field. Denylist
        // Keywords (own Any-shape) and TimeCreated (no Emitter.EmitContains arm) so Lowerer acceptance stays in
        // lock-step with the emitter; non-string fields are formatted to text inside the emitter.
        if (IsCaseInsensitiveMatch(call.Name, "Contains")
            && call.Target is IdentifierSyntax containsTarget
            && PropertyResolver.TryResolve(containsTarget.Name, out var containsField, out _)
            && containsField is not (ResolvedEventField.Keywords or ResolvedEventField.TimeCreated))
        {
            var (needle, ignoreCase) = ParseContainsArgs(call.Arguments, call.Position);

            return new ContainsNode(containsField, needle, ignoreCase);
        }

        // P.ToString().Contains(needle, OIC) — legacy formatter shape, kept for back-compat with stored filters.
        // Same Contains-supported-field denylist as the bare branch so Lowerer acceptance stays in lock-step with
        // Emitter.EmitContains (Keywords has its own Any-shape; TimeCreated has no emit arm).
        if (IsCaseInsensitiveMatch(call.Name, "Contains")
            && call.Target is MethodCallSyntax toStringCall
            && IsCaseInsensitiveMatch(toStringCall.Name, "ToString")
            && toStringCall.Arguments.Count == 0
            && toStringCall.Target is IdentifierSyntax toStringPropTarget
            && PropertyResolver.TryResolve(toStringPropTarget.Name, out var toStringField, out _)
            && toStringField is not (ResolvedEventField.Keywords or ResolvedEventField.TimeCreated))
        {
            var (needle, ignoreCase) = ParseContainsArgs(call.Arguments, call.Position);

            return new ContainsNode(toStringField, needle, ignoreCase);
        }

        // Keywords.Any(lambda)
        if (IsCaseInsensitiveMatch(call.Name, "Any")
            && call.Target is IdentifierSyntax keywordsTarget
            && PropertyResolver.TryResolve(keywordsTarget.Name, out var anyField, out _)
            && anyField == ResolvedEventField.Keywords
            && call.Arguments is [LambdaSyntax lambda])
        {
            return LowerKeywordsAnyLambda(lambda);
        }

        throw new LowerException(
            $"Unsupported method call '{call.Name}' (position {call.Position}).");
    }

    // Normalizes `!` over EventData so a NotNode never wraps an EventData node (presence-required must hold at any
    // depth): comparison flips Equal<->NotEqual, Contains toggles Negated; the unrepresentable none-of and any group
    // that contains an EventData node are rejected. Non-EventData operands wrap in NotNode as before.
    private static SemanticNode LowerNegation(UnarySyntax neg)
    {
        var inner = Lower(neg.Operand);

        switch (inner)
        {
            case EventDataComparisonNode comparison:
                var flipped = comparison.Op == FilterBinaryOperator.Equal
                    ? FilterBinaryOperator.NotEqual
                    : FilterBinaryOperator.Equal;

                return new EventDataComparisonNode(comparison.FieldName, flipped, comparison.Literal);
            case EventDataContainsNode contains:
                return new EventDataContainsNode(
                    contains.FieldName,
                    contains.Needle,
                    contains.IgnoreCase,
                    !contains.Negated);
            case EventDataMultiEqualsNode:
                throw new LowerException(
                    $"Negating an EventData any-of match is not supported; use separate '!=' conditions (position {neg.Position}).");
            default:
                if (ContainsEventDataNode(inner))
                {
                    throw new LowerException(
                        $"Negating a group that contains EventData conditions is not supported; negate each EventData condition individually (position {neg.Position}).");
                }

                return new NotNode(inner);
        }
    }

    private static FilterBinaryOperator MapBinaryOp(TokenKind op) =>
        op switch
        {
            TokenKind.EqEq => FilterBinaryOperator.Equal,
            TokenKind.NotEq => FilterBinaryOperator.NotEqual,
            TokenKind.Lt => FilterBinaryOperator.LessThan,
            TokenKind.Gt => FilterBinaryOperator.GreaterThan,
            TokenKind.Le => FilterBinaryOperator.LessThanOrEqual,
            TokenKind.Ge => FilterBinaryOperator.GreaterThanOrEqual,
            _ => throw new LowerException($"Internal: unsupported binary operator '{op}'.")
        };

    private static (string Needle, bool IgnoreCase) ParseContainsArgs(
        IReadOnlyList<SyntaxNode> args,
        int position)
    {
        if (args.Count is < 1 or > 2)
        {
            throw new LowerException(
                $".Contains expects 1 or 2 arguments, got {args.Count} (position {position}).");
        }

        if (args[0] is not LiteralSyntax { Kind: LiteralKind.String } lit)
        {
            throw new LowerException(
                $"First argument to .Contains must be a string literal (position {args[0].Position}).");
        }

        var ignoreCase = false;

        if (args.Count == 2)
        {
            if (!IsStringComparisonOIC(args[1]))
            {
                throw new LowerException(
                    $"Second argument to .Contains must be StringComparison.OrdinalIgnoreCase (position {args[1].Position}).");
            }

            ignoreCase = true;
        }

        return (lit.Text, ignoreCase);
    }

    private static (ResolvedEventField Field, FieldExpressionShape Shape) ResolveFieldOrToString(
        SyntaxNode expr,
        int position)
    {
        switch (expr)
        {
            case IdentifierSyntax id:
                if (!PropertyResolver.TryResolve(id.Name, out var directField, out _))
                {
                    throw new LowerException($"Unknown property '{id.Name}' (position {id.Position}).");
                }

                return (directField, FieldExpressionShape.DirectIdentifier);
            case MethodCallSyntax mc when IsCaseInsensitiveMatch(mc.Name, "ToString")
                && mc.Arguments.Count == 0
                && mc.Target is IdentifierSyntax targetId:
                if (!PropertyResolver.TryResolve(targetId.Name, out var toStringField, out _))
                {
                    throw new LowerException(
                        $"Unknown property '{targetId.Name}' (position {targetId.Position}).");
                }

                return (toStringField, FieldExpressionShape.ToStringCall);
            default:
                throw new LowerException(
                    $"Left-hand side of comparison must be a property reference (position {position}).");
        }
    }

    private static bool TryLowerUserIdRhs(SyntaxNode rhs, out SemanticNode lowered)
    {
        lowered = null!;

        // !UserId.Value.Contains(...)
        if (rhs is UnarySyntax { Op: TokenKind.Bang } neg)
        {
            if (TryLowerUserIdRhs(neg.Operand, out var inner))
            {
                lowered = new NotNode(inner);

                return true;
            }

            return false;
        }

        // UserId.Value == lit  -or-  UserId.Value != lit
        if (rhs is BinarySyntax { Op: TokenKind.EqEq or TokenKind.NotEq, Left: MemberAccessSyntax mem } cmp
            && IsUserIdValue(mem)
            && cmp.Right is LiteralSyntax { Kind: LiteralKind.String } lit)
        {
            var op = cmp.Op == TokenKind.EqEq ? FilterBinaryOperator.Equal : FilterBinaryOperator.NotEqual;
            lowered = new ComparisonNode(ResolvedEventField.UserId, op, TypedLiteral.String(lit.Text));

            return true;
        }

        // UserId.Value.Contains(needle, OIC)
        if (rhs is MethodCallSyntax mc
            && IsCaseInsensitiveMatch(mc.Name, "Contains")
            && mc.Target is MemberAccessSyntax mcMem
            && IsUserIdValue(mcMem))
        {
            var (needle, ignoreCase) = ParseContainsArgs(mc.Arguments, mc.Position);
            lowered = new ContainsNode(ResolvedEventField.UserId, needle, ignoreCase);

            return true;
        }

        return false;
    }

    // Returns true for a well-formed `EventData["FieldName"]` access (case-insensitive target, Ordinal field name);
    // throws a descriptive LowerException for a malformed EventData index (non-string / empty key); returns false for
    // any non-EventData expression.
    private static bool TryResolveEventDataField(SyntaxNode expr, [NotNullWhen(true)] out string? fieldName)
    {
        fieldName = null;

        if (expr is not IndexAccessSyntax index
            || index.Target is not IdentifierSyntax id
            || !IsCaseInsensitiveMatch(id.Name, "EventData"))
        {
            return false;
        }

        if (index.Argument is not LiteralSyntax { Kind: LiteralKind.String } key)
        {
            throw new LowerException(
                $"EventData field name must be a string literal (position {index.Argument.Position}).");
        }

        if (string.IsNullOrWhiteSpace(key.Text))
        {
            throw new LowerException($"EventData field name must not be empty (position {key.Position}).");
        }

        fieldName = key.Text;

        return true;
    }

    private sealed class LowerException(string message) : Exception(message);
}
