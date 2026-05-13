// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.Filtering.Parsing;

/// <summary>
///     Pratt-style precedence-climbing parser over a flat token list. Produces a <see cref="SyntaxNode" /> tree.
///     Operator precedence matches C# (highest first): postfix <c>.</c> / <c>()</c>; unary <c>!</c>; relational
///     <c>&lt; &gt; &lt;= &gt;=</c>; equality <c>== !=</c>; <c>&amp;&amp;</c>; <c>||</c>.
/// </summary>
internal static class Parser
{
    public static bool TryParse(IReadOnlyList<Token> tokens, out SyntaxNode? root, [NotNullWhen(false)] out string? error)
    {
        root = null;
        error = null;

        if (tokens is null || tokens.Count == 0)
        {
            error = "Token stream is empty.";

            return false;
        }

        var state = new ParserState(tokens);

        try
        {
            root = ParseExpression(state, 0);

            if (state.Current.Kind != TokenKind.End)
            {
                error = $"Unexpected token '{state.Current.Text}' (position {state.Current.Position}).";
                root = null;

                return false;
            }

            return true;
        }
        catch (ParseException ex)
        {
            error = ex.Message;
            root = null;

            return false;
        }
    }

    private static void Expect(ParserState state, TokenKind kind)
    {
        if (state.Current.Kind != kind)
        {
            throw new ParseException(
                $"Expected '{kind}' but got '{state.Current.Text}' (position {state.Current.Position}).");
        }

        state.Advance();
    }

    private static int GetBinaryPrecedence(TokenKind kind) =>
        kind switch
        {
            TokenKind.OrOr => 1,
            TokenKind.AndAnd => 2,
            TokenKind.EqEq or TokenKind.NotEq => 3,
            TokenKind.Lt or TokenKind.Gt or TokenKind.Le or TokenKind.Ge => 4,
            _ => 0
        };

    private static IReadOnlyList<SyntaxNode> ParseArgumentList(ParserState state)
    {
        if (state.Current.Kind == TokenKind.RParen) { return []; }

        var args = new List<SyntaxNode>();

        while (true)
        {
            args.Add(ParseExpression(state, 0));

            if (state.Current.Kind == TokenKind.Comma)
            {
                state.Advance();

                continue;
            }

            break;
        }

        return args;
    }

    private static SyntaxNode ParseArrayCreation(ParserState state)
    {
        var position = state.Current.Position;
        state.Advance(); // 'new'
        Expect(state, TokenKind.LBracket);
        Expect(state, TokenKind.RBracket);
        Expect(state, TokenKind.LBrace);

        var elements = new List<SyntaxNode>();

        if (state.Current.Kind != TokenKind.RBrace)
        {
            while (true)
            {
                if (elements.Count >= ParseLimits.MaxArrayElements)
                {
                    throw new ParseException(
                        $"Array literal exceeds maximum element count of {ParseLimits.MaxArrayElements}.");
                }

                elements.Add(ParseExpression(state, 0));

                if (state.Current.Kind == TokenKind.Comma)
                {
                    state.Advance();

                    continue;
                }

                break;
            }
        }

        Expect(state, TokenKind.RBrace);

        return new ArrayCreationSyntax
        {
            Elements = elements,
            Position = position
        };
    }

    private static SyntaxNode ParseExpression(ParserState state, int minPrecedence)
    {
        state.EnterDepth();
        try
        {
            var left = ParseUnary(state);

            while (true)
            {
                var op = state.Current.Kind;
                var precedence = GetBinaryPrecedence(op);

                if (precedence == 0 || precedence < minPrecedence) { break; }

                var position = state.Current.Position;
                state.Advance();
                var right = ParseExpression(state, precedence + 1);

                left = new BinarySyntax
                {
                    Op = op,
                    Left = left,
                    Right = right,
                    Position = position
                };
            }

            return left;
        }
        finally
        {
            state.ExitDepth();
        }
    }

    private static SyntaxNode ParsePostfix(ParserState state)
    {
        var node = ParsePrimary(state);

        while (true)
        {
            if (state.Current.Kind == TokenKind.Dot)
            {
                var dotPosition = state.Current.Position;
                state.Advance();

                if (state.Current.Kind != TokenKind.Identifier)
                {
                    throw new ParseException(
                        $"Expected member name after '.' (position {state.Current.Position}).");
                }

                var name = state.Current.Text;
                state.Advance();

                if (state.Current.Kind == TokenKind.LParen)
                {
                    state.Advance();
                    var args = ParseArgumentList(state);
                    Expect(state, TokenKind.RParen);

                    node = new MethodCallSyntax
                    {
                        Target = node,
                        Name = name,
                        Arguments = args,
                        Position = dotPosition
                    };
                }
                else
                {
                    node = new MemberAccessSyntax
                    {
                        Target = node,
                        Name = name,
                        Position = dotPosition
                    };
                }
            }
            else
            {
                break;
            }
        }

        return node;
    }

    private static SyntaxNode ParsePrimary(ParserState state)
    {
        var token = state.Current;

        switch (token.Kind)
        {
            case TokenKind.Identifier:
                if (state.Peek(1).Kind == TokenKind.FatArrow)
                {
                    state.Advance();
                    state.Advance();
                    var body = ParseExpression(state, 0);

                    return new LambdaSyntax
                    {
                        ParameterName = token.Text,
                        Body = body,
                        Position = token.Position
                    };
                }

                state.Advance();

                return new IdentifierSyntax
                {
                    Name = token.Text,
                    Position = token.Position
                };
            case TokenKind.String:
                state.Advance();

                return new LiteralSyntax
                {
                    Kind = LiteralKind.String,
                    Text = token.Text,
                    Position = token.Position
                };
            case TokenKind.Int:
                state.Advance();

                return new LiteralSyntax
                {
                    Kind = LiteralKind.Int,
                    Text = token.Text,
                    Position = token.Position
                };
            case TokenKind.Null:
                state.Advance();

                return new LiteralSyntax
                {
                    Kind = LiteralKind.Null,
                    Text = "null",
                    Position = token.Position
                };
            case TokenKind.LParen:
                state.Advance();
                var inner = ParseExpression(state, 0);
                Expect(state, TokenKind.RParen);

                return inner;
            case TokenKind.New:
                return ParseArrayCreation(state);
            default:
                throw new ParseException(
                    $"Unexpected token '{token.Text}' (position {token.Position}).");
        }
    }

    private static SyntaxNode ParseUnary(ParserState state)
    {
        if (state.Current.Kind == TokenKind.Bang)
        {
            var position = state.Current.Position;
            state.Advance();
            var operand = ParseUnary(state);

            return new UnarySyntax
            {
                Op = TokenKind.Bang,
                Operand = operand,
                Position = position
            };
        }

        return ParsePostfix(state);
    }

    private sealed class ParseException(string message) : Exception(message);

    private sealed class ParserState
    {
        private readonly IReadOnlyList<Token> _tokens;

        private int _depth;

        public ParserState(IReadOnlyList<Token> tokens) => _tokens = tokens;

        public Token Current => _tokens[Index];

        public int Index { get; private set; }

        public void Advance()
        {
            if (Index < _tokens.Count - 1) { Index++; }
        }

        public void EnterDepth()
        {
            _depth++;

            if (_depth > ParseLimits.MaxParseDepth)
            {
                throw new ParseException(
                    $"Filter expression exceeds maximum nesting depth of {ParseLimits.MaxParseDepth}.");
            }
        }

        public void ExitDepth() => _depth--;

        public Token Peek(int offset) =>
            Index + offset < _tokens.Count ? _tokens[Index + offset] : _tokens[^1];
    }
}
