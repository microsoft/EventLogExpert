// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

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
                if (TryLowerUserIdGuarded(andNode, out var userIdLowered))
                {
                    return userIdLowered;
                }

                return new AndNode(Lower(andNode.Left), Lower(andNode.Right));
            case BinarySyntax bin when IsComparisonOp(bin.Op):
                return LowerComparison(bin);
            case UnarySyntax { Op: TokenKind.Bang } neg:
                return new NotNode(Lower(neg.Operand));
            case MethodCallSyntax call:
                return LowerMethodCall(call);
            case BinarySyntax bin:
                throw new LowerException($"Operator '{bin.Op}' is not supported here (position {bin.Position}).");
            default:
                throw new LowerException($"Unsupported expression at position {node.Position}.");
        }
    }

    private static SemanticNode LowerComparison(BinarySyntax bin)
    {
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
            var argField = ResolveFieldOrToString(call.Arguments[0], call.Arguments[0].Position).Field;

            return new MultiEqualsNode(argField, elements);
        }

        // P.Contains(needle, StringComparison.OrdinalIgnoreCase) for string properties
        if (IsCaseInsensitiveMatch(call.Name, "Contains")
            && call.Target is IdentifierSyntax stringPropTarget
            && PropertyResolver.TryResolve(stringPropTarget.Name, out var stringField, out var kind)
            && kind == TypedLiteralKind.String
            && stringField != ResolvedEventField.Keywords)
        {
            var (needle, ignoreCase) = ParseContainsArgs(call.Arguments, call.Position);

            return new ContainsNode(stringField, needle, ignoreCase);
        }

        // P.ToString().Contains(needle, OIC) for Id, ActivityId, etc. (formatter shape)
        if (IsCaseInsensitiveMatch(call.Name, "Contains")
            && call.Target is MethodCallSyntax toStringCall
            && IsCaseInsensitiveMatch(toStringCall.Name, "ToString")
            && toStringCall.Arguments.Count == 0
            && toStringCall.Target is IdentifierSyntax toStringPropTarget
            && PropertyResolver.TryResolve(toStringPropTarget.Name, out var toStringField, out _))
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

    private static bool TryLowerUserIdGuarded(BinarySyntax andNode, out SemanticNode lowered)
    {
        lowered = null!;

        // LHS must be: UserId != null  (or null != UserId)
        if (andNode.Left is not BinarySyntax { Op: TokenKind.NotEq } leftBin
            || !IsNullCheckOnIdentifier(leftBin, "UserId"))
        {
            return false;
        }

        return TryLowerUserIdRhs(andNode.Right, out lowered);
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

    private sealed class LowerException(string message) : Exception(message);
}
