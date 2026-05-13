// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Filtering.Parsing;

/// <summary>Discrete token kinds the <see cref="Tokenizer" /> emits.</summary>
internal enum TokenKind
{
    Identifier,
    String,
    Int,
    LParen,
    RParen,
    LBracket,
    RBracket,
    LBrace,
    RBrace,
    Dot,
    Comma,
    EqEq,
    NotEq,
    Lt,
    Gt,
    Le,
    Ge,
    AndAnd,
    OrOr,
    Bang,
    FatArrow,
    New,
    Null,
    End
}

/// <summary>
///     A single lexical token. <see cref="Position" /> is the zero-based source character index for diagnostics.
///     <see cref="Text" /> is the raw identifier/integer text or the <em>decoded</em> string body for
///     <see cref="TokenKind.String" /> tokens (escapes resolved by the tokenizer).
/// </summary>
internal readonly record struct Token(TokenKind Kind, string Text, int Position);
