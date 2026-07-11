// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Structured;
using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Parsing;
using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.Filtering.Lowering;

/// <summary>
///     Walks a <see cref="SyntaxNode" /> tree and emits a <see cref="FilterNode" /> by recognizing the closed
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

    public static bool TryLower(SyntaxNode root, out FilterNode? filterNode, [NotNullWhen(false)] out string? error)
    {
        filterNode = null;
        error = null;

        try
        {
            filterNode = Lower(root);

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
            ResolvedEventField.ActivityId or ResolvedEventField.RelatedActivityId => TypedLiteralKind.Guid,
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

    private static bool ContainsEventDataNode(FilterNode node) =>
        node switch
        {
            EventDataComparisonNode or EventDataContainsNode or EventDataMultiEqualsNode or EventDataMultiContainsNode => true,
            AndNode and => ContainsEventDataNode(and.Left) || ContainsEventDataNode(and.Right),
            OrNode or => ContainsEventDataNode(or.Left) || ContainsEventDataNode(or.Right),
            NotNode not => ContainsEventDataNode(not.Operand),
            _ => false
        };

    private static bool ContainsUserDataNode(FilterNode node) =>
        node switch
        {
            UserDataComparisonNode or UserDataContainsNode or UserDataMultiEqualsNode or UserDataMultiContainsNode => true,
            AndNode and => ContainsUserDataNode(and.Left) || ContainsUserDataNode(and.Right),
            OrNode or => ContainsUserDataNode(or.Left) || ContainsUserDataNode(or.Right),
            NotNode not => ContainsUserDataNode(not.Operand),
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

    private static string GetUserDataLiteralText(LiteralSyntax literal) =>
        literal.Kind switch
        {
            LiteralKind.String => literal.Text,
            LiteralKind.Int => literal.Text,
            _ => throw new LowerException(
                $"UserData comparison value must be a string or integer literal (position {literal.Position}).")
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

    private static bool IsMultiContainsField(ResolvedEventField field) =>
        field is ResolvedEventField.ComputerName
            or ResolvedEventField.Description
            or ResolvedEventField.Level
            or ResolvedEventField.LogName
            or ResolvedEventField.Opcode
            or ResolvedEventField.Source
            or ResolvedEventField.TaskCategory
            or ResolvedEventField.UserId
            or ResolvedEventField.Xml;

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

    private static FilterNode Lower(SyntaxNode node)
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

    private static FilterNode LowerAndChain(BinarySyntax andNode)
    {
        var flat = new List<SyntaxNode>();
        FlattenAnd(andNode, flat);

        var lowered = new List<FilterNode>(flat.Count);
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

    private static FilterNode LowerComparison(BinarySyntax bin)
    {
        if (TryResolveEventDataField(bin.Left, out var eventDataFieldName))
        {
            return LowerEventDataComparison(eventDataFieldName, bin);
        }

        if (TryResolveUserDataField(bin.Left, out var userDataPath))
        {
            return LowerUserDataComparison(userDataPath, bin);
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

    private static FilterNode LowerEventDataComparison(string fieldName, BinarySyntax bin)
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

    private static FilterNode LowerKeywordsAnyLambda(LambdaSyntax lambda)
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

    private static FilterNode LowerMethodCall(MethodCallSyntax call)
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

            // UserData any-of, recognized before ResolveFieldOrToString for the same reason.
            if (TryResolveUserDataField(call.Arguments[0], out var userDataAnyOfPath))
            {
                return new UserDataMultiEqualsNode(userDataAnyOfPath, elements);
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

        // UserData["Event/UserData/..."].Contains(needle, OIC)
        if (IsCaseInsensitiveMatch(call.Name, "Contains")
            && TryResolveUserDataField(call.Target, out var userDataContainsPath))
        {
            var (needle, ignoreCase) = ParseContainsArgs(call.Arguments, call.Position);

            return new UserDataContainsNode(userDataContainsPath, needle, ignoreCase, negated: false);
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

        // (new[] {...}).Any(e => F.Contains(e, OIC)) — string "contains any of" over a scalar string field.
        if (IsCaseInsensitiveMatch(call.Name, "Any")
            && call is { Target: ArrayCreationSyntax anyContainsArray, Arguments: [LambdaSyntax anyContainsLambda] })
        {
            return LowerMultiContainsLambda(anyContainsArray, anyContainsLambda, call.Position);
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

    // Lowers `(new[] {...}).Any(e => field.Contains(e, [OIC]))` to a scalar "contains any of" node. The body must be
    // `field.Contains(param, ...)` over a supported string field (kept in lock-step with Emitter.EmitMultiContains),
    // with the lambda parameter as the first Contains argument.
    private static FilterNode LowerMultiContainsLambda(ArrayCreationSyntax array, LambdaSyntax lambda, int position)
    {
        // Body must be `<target>.Contains(param, [OIC])` with the lambda parameter as the first Contains argument.
        if (lambda.Body is not MethodCallSyntax contains
            || !IsCaseInsensitiveMatch(contains.Name, "Contains")
            || contains.Arguments.Count is < 1 or > 2
            || !IsLambdaParam(contains.Arguments[0], lambda.ParameterName))
        {
            throw new LowerException(
                $"Unsupported .Any lambda; expected `<field>.Contains({lambda.ParameterName}, StringComparison.OrdinalIgnoreCase)` (position {lambda.Body.Position}).");
        }

        var ignoreCase = false;

        if (contains.Arguments.Count == 2)
        {
            if (!IsStringComparisonOIC(contains.Arguments[1]))
            {
                throw new LowerException(
                    $"Second argument to .Contains must be StringComparison.OrdinalIgnoreCase (position {contains.Arguments[1].Position}).");
            }

            ignoreCase = true;
        }

        var needles = ExtractStringArray(array, position);

        // EventData / UserData indexer targets (presence-required, positive-only) are recognized before the scalar
        // field so an IndexAccess receiver is not misread as a property identifier.
        if (TryResolveEventDataField(contains.Target, out var eventDataName))
        {
            return new EventDataMultiContainsNode(eventDataName, needles, ignoreCase);
        }

        if (TryResolveUserDataField(contains.Target, out var userDataPath))
        {
            return new UserDataMultiContainsNode(userDataPath, needles, ignoreCase);
        }

        // Scalar string field target (kept in lock-step with Emitter.EmitMultiContains).
        if (contains.Target is IdentifierSyntax fieldId
            && PropertyResolver.TryResolve(fieldId.Name, out var field, out _)
            && IsMultiContainsField(field))
        {
            return new MultiContainsNode(field, needles, ignoreCase);
        }

        throw new LowerException(
            $"Unsupported .Any lambda target; expected a string field, EventData[\"...\"], or UserData[\"...\"] (position {contains.Target.Position}).");
    }

    // Normalizes `!` over EventData so a NotNode never wraps an EventData node (presence-required must hold at any
    // depth): comparison flips Equal<->NotEqual, Contains toggles Negated; the unrepresentable none-of and any group
    // that contains an EventData node are rejected. Non-EventData operands wrap in NotNode as before.
    private static FilterNode LowerNegation(UnarySyntax neg)
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
            case EventDataMultiEqualsNode or EventDataMultiContainsNode:
                throw new LowerException(
                    $"Negating an EventData any-of match is not supported; use separate '!=' conditions (position {neg.Position}).");
            case UserDataComparisonNode userComparison:
                var userFlipped = userComparison.Op == FilterBinaryOperator.Equal
                    ? FilterBinaryOperator.NotEqual
                    : FilterBinaryOperator.Equal;

                return new UserDataComparisonNode(userComparison.CanonicalPath, userFlipped, userComparison.Literal);
            case UserDataContainsNode userContains:
                return new UserDataContainsNode(
                    userContains.CanonicalPath,
                    userContains.Needle,
                    userContains.IgnoreCase,
                    !userContains.Negated);
            case UserDataMultiEqualsNode or UserDataMultiContainsNode:
                throw new LowerException(
                    $"Negating a UserData any-of match is not supported; use separate '!=' conditions (position {neg.Position}).");
            case MultiEqualsNode multiEquals:
                return new MultiEqualsNode(multiEquals.Field, multiEquals.Values, !multiEquals.Negated);
            case MultiContainsNode multiContains:
                return new MultiContainsNode(
                    multiContains.Field,
                    multiContains.Values,
                    multiContains.IgnoreCase,
                    !multiContains.Negated);
            default:
                if (ContainsEventDataNode(inner))
                {
                    throw new LowerException(
                        $"Negating a group that contains EventData conditions is not supported; negate each EventData condition individually (position {neg.Position}).");
                }

                if (ContainsUserDataNode(inner))
                {
                    throw new LowerException(
                        $"Negating a group that contains UserData conditions is not supported; negate each UserData condition individually (position {neg.Position}).");
                }

                return new NotNode(inner);
        }
    }

    private static FilterNode LowerUserDataComparison(string canonicalPath, BinarySyntax bin)
    {
        if (bin.Op is not (TokenKind.EqEq or TokenKind.NotEq))
        {
            throw new LowerException(
                $"Operator '{bin.Op}' is not supported on UserData paths; use '==' or '!=' (position {bin.Position}).");
        }

        if (bin.Right is not LiteralSyntax literal)
        {
            throw new LowerException(
                $"Right-hand side of a UserData comparison must be a literal (position {bin.Right.Position}).");
        }

        var op = bin.Op == TokenKind.EqEq ? FilterBinaryOperator.Equal : FilterBinaryOperator.NotEqual;

        return new UserDataComparisonNode(canonicalPath, op, GetUserDataLiteralText(literal));
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

    private static bool TryLowerUserIdRhs(SyntaxNode rhs, out FilterNode lowered)
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

        if (expr is not IndexAccessSyntax { Target: IdentifierSyntax id } index
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

    // True for a well-formed `UserData["path"]` access (normalizing/validating the canonical path); throws a
    // descriptive LowerException for a malformed UserData index; false for any non-UserData expression.
    private static bool TryResolveUserDataField(SyntaxNode expr, [NotNullWhen(true)] out string? canonicalPath)
    {
        canonicalPath = null;

        if (expr is not IndexAccessSyntax { Target: IdentifierSyntax id } index
            || !IsCaseInsensitiveMatch(id.Name, "UserData"))
        {
            return false;
        }

        if (index.Argument is not LiteralSyntax { Kind: LiteralKind.String } key)
        {
            throw new LowerException(
                $"UserData path must be a string literal (position {index.Argument.Position}).");
        }

        if (!UserDataFieldPath.TryNormalize(key.Text, out var normalized, out var pathError))
        {
            throw new LowerException($"{pathError} (position {key.Position}).");
        }

        canonicalPath = normalized;

        return true;
    }

    private sealed class LowerException(string message) : Exception(message);
}
