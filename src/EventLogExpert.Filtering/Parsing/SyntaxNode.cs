// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Filtering.Parsing;

/// <summary>
///     Common base for the small expression-shaped syntax tree the <see cref="Parser" /> produces. The
///     <see cref="Lowerer" /> walks this tree and emits a domain-shaped <see cref="Semantic.SemanticNode" />.
/// </summary>
internal abstract class SyntaxNode
{
    public required int Position { get; init; }
}

internal sealed class IdentifierSyntax : SyntaxNode
{
    public required string Name { get; init; }
}

internal enum LiteralKind
{
    String,
    Int,
    Null
}

internal sealed class LiteralSyntax : SyntaxNode
{
    public required LiteralKind Kind { get; init; }

    public required string Text { get; init; }
}

internal sealed class BinarySyntax : SyntaxNode
{
    public required SyntaxNode Left { get; init; }

    public required TokenKind Op { get; init; }

    public required SyntaxNode Right { get; init; }
}

internal sealed class UnarySyntax : SyntaxNode
{
    public required TokenKind Op { get; init; }

    public required SyntaxNode Operand { get; init; }
}

internal sealed class MemberAccessSyntax : SyntaxNode
{
    public required string Name { get; init; }

    public required SyntaxNode Target { get; init; }
}

internal sealed class MethodCallSyntax : SyntaxNode
{
    public required IReadOnlyList<SyntaxNode> Arguments { get; init; }

    public required string Name { get; init; }

    public required SyntaxNode Target { get; init; }
}

internal sealed class IndexAccessSyntax : SyntaxNode
{
    public required SyntaxNode Argument { get; init; }

    public required SyntaxNode Target { get; init; }
}

internal sealed class ArrayCreationSyntax : SyntaxNode
{
    public required IReadOnlyList<SyntaxNode> Elements { get; init; }
}

internal sealed class LambdaSyntax : SyntaxNode
{
    public required SyntaxNode Body { get; init; }

    public required string ParameterName { get; init; }
}
