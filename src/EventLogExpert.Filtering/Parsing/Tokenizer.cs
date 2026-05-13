// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace EventLogExpert.Filtering.Parsing;

/// <summary>
///     Single-pass char iterator producing a flat <see cref="Token" /> list. Skips ASCII whitespace (
///     <c>' ' '\t' '\r' '\n'</c>); decodes the five string escapes <c>\\ \" \r \n \t</c>; recognizes the keywords
///     <c>new</c> and <c>null</c>. Enforces <see cref="ParseLimits" /> ceilings.
/// </summary>
internal static class Tokenizer
{
    public static bool TryTokenize(string source, out List<Token> tokens, [NotNullWhen(false)] out string? error)
    {
        tokens = [];
        error = null;

        if (source is null)
        {
            error = "Filter expression is null.";

            return false;
        }

        if (source.Length > ParseLimits.MaxInputLength)
        {
            error = $"Filter expression exceeds maximum length of {ParseLimits.MaxInputLength} characters.";

            return false;
        }

        var index = 0;

        while (index < source.Length)
        {
            var ch = source[index];

            if (ch is ' ' or '\t' or '\r' or '\n')
            {
                index++;

                continue;
            }

            if (tokens.Count >= ParseLimits.MaxTokens)
            {
                error = $"Filter expression exceeds maximum token count of {ParseLimits.MaxTokens}.";

                return false;
            }

            var startPosition = index;

            switch (ch)
            {
                case '(':
                    tokens.Add(new Token(TokenKind.LParen, "(", startPosition));
                    index++;

                    break;
                case ')':
                    tokens.Add(new Token(TokenKind.RParen, ")", startPosition));
                    index++;

                    break;
                case '[':
                    tokens.Add(new Token(TokenKind.LBracket, "[", startPosition));
                    index++;

                    break;
                case ']':
                    tokens.Add(new Token(TokenKind.RBracket, "]", startPosition));
                    index++;

                    break;
                case '{':
                    tokens.Add(new Token(TokenKind.LBrace, "{", startPosition));
                    index++;

                    break;
                case '}':
                    tokens.Add(new Token(TokenKind.RBrace, "}", startPosition));
                    index++;

                    break;
                case '.':
                    tokens.Add(new Token(TokenKind.Dot, ".", startPosition));
                    index++;

                    break;
                case ',':
                    tokens.Add(new Token(TokenKind.Comma, ",", startPosition));
                    index++;

                    break;
                case '=':
                    if (PeekAt(source, index + 1) == '=')
                    {
                        tokens.Add(new Token(TokenKind.EqEq, "==", startPosition));
                        index += 2;
                    }
                    else if (PeekAt(source, index + 1) == '>')
                    {
                        tokens.Add(new Token(TokenKind.FatArrow, "=>", startPosition));
                        index += 2;
                    }
                    else
                    {
                        error = FormatError("Expected '==' or '=>' after '='", startPosition);

                        return false;
                    }

                    break;
                case '!':
                    if (PeekAt(source, index + 1) == '=')
                    {
                        tokens.Add(new Token(TokenKind.NotEq, "!=", startPosition));
                        index += 2;
                    }
                    else
                    {
                        tokens.Add(new Token(TokenKind.Bang, "!", startPosition));
                        index++;
                    }

                    break;
                case '<':
                    if (PeekAt(source, index + 1) == '=')
                    {
                        tokens.Add(new Token(TokenKind.Le, "<=", startPosition));
                        index += 2;
                    }
                    else
                    {
                        tokens.Add(new Token(TokenKind.Lt, "<", startPosition));
                        index++;
                    }

                    break;
                case '>':
                    if (PeekAt(source, index + 1) == '=')
                    {
                        tokens.Add(new Token(TokenKind.Ge, ">=", startPosition));
                        index += 2;
                    }
                    else
                    {
                        tokens.Add(new Token(TokenKind.Gt, ">", startPosition));
                        index++;
                    }

                    break;
                case '&':
                    if (PeekAt(source, index + 1) != '&')
                    {
                        error = FormatError("Expected '&&' after '&'", startPosition);

                        return false;
                    }

                    tokens.Add(new Token(TokenKind.AndAnd, "&&", startPosition));
                    index += 2;

                    break;
                case '|':
                    if (PeekAt(source, index + 1) != '|')
                    {
                        error = FormatError("Expected '||' after '|'", startPosition);

                        return false;
                    }

                    tokens.Add(new Token(TokenKind.OrOr, "||", startPosition));
                    index += 2;

                    break;
                case '"':
                    if (!TryReadString(source, ref index, out var decoded, out error))
                    {
                        return false;
                    }

                    tokens.Add(new Token(TokenKind.String, decoded, startPosition));

                    break;
                default:
                    if (IsIdentifierStart(ch))
                    {
                        var identifier = ReadIdentifier(source, ref index);
                        var kind = identifier switch
                        {
                            "new" => TokenKind.New,
                            "null" => TokenKind.Null,
                            _ => TokenKind.Identifier
                        };
                        tokens.Add(new Token(kind, identifier, startPosition));
                    }
                    else if (IsDigit(ch))
                    {
                        var number = ReadInt(source, ref index);
                        tokens.Add(new Token(TokenKind.Int, number, startPosition));
                    }
                    else
                    {
                        error = FormatError($"Unexpected character '{ch}'", startPosition);

                        return false;
                    }

                    break;
            }
        }

        tokens.Add(new Token(TokenKind.End, string.Empty, source.Length));

        return true;
    }

    private static string DecodeEscapes(string source, int start, int length, int openPosition, out string? error)
    {
        error = null;
        var builder = new StringBuilder(length);
        var end = start + length;
        var i = start;

        while (i < end)
        {
            var ch = source[i];

            if (ch == '\\' && i + 1 < end)
            {
                var next = source[i + 1];

                switch (next)
                {
                    case '\\':
                        builder.Append('\\');

                        break;
                    case '"':
                        builder.Append('"');

                        break;
                    case 'r':
                        builder.Append('\r');

                        break;
                    case 'n':
                        builder.Append('\n');

                        break;
                    case 't':
                        builder.Append('\t');

                        break;
                    default:
                        error = FormatError($"Unsupported escape sequence '\\{next}' in string literal.", openPosition);

                        return string.Empty;
                }

                i += 2;
            }
            else
            {
                builder.Append(ch);
                i++;
            }
        }

        return builder.ToString();
    }

    private static string FormatError(string message, int position) =>
        $"{message} (position {position}).";

    private static bool IsDigit(char ch) => ch is >= '0' and <= '9';

    private static bool IsIdentifierPart(char ch) => char.IsLetterOrDigit(ch) || ch == '_';

    private static bool IsIdentifierStart(char ch) => char.IsLetter(ch) || ch == '_';

    private static char PeekAt(string source, int index) =>
        index < source.Length ? source[index] : '\0';

    private static string ReadIdentifier(string source, ref int index)
    {
        var start = index;

        while (index < source.Length && IsIdentifierPart(source[index])) { index++; }

        return source.Substring(start, index - start);
    }

    private static string ReadInt(string source, ref int index)
    {
        var start = index;

        while (index < source.Length && IsDigit(source[index])) { index++; }

        return source.Substring(start, index - start);
    }

    private static bool TryReadString(string source, ref int index, out string decoded, [NotNullWhen(false)] out string? error)
    {
        decoded = string.Empty;
        error = null;

        var openPosition = index;
        index++;
        var hasEscape = false;
        var contentStart = index;

        while (index < source.Length)
        {
            var ch = source[index];

            if (ch == '"')
            {
                var length = index - contentStart;

                if (length > ParseLimits.MaxStringLiteralLength)
                {
                    error = FormatError(
                        $"String literal exceeds maximum length of {ParseLimits.MaxStringLiteralLength} characters.",
                        openPosition);

                    return false;
                }

                decoded = hasEscape
                    ? DecodeEscapes(source, contentStart, length, openPosition, out error)
                    : source.Substring(contentStart, length);
                index++;

                return error is null;
            }

            if (ch == '\\')
            {
                hasEscape = true;

                if (index + 1 >= source.Length)
                {
                    error = FormatError("Unterminated string literal.", openPosition);

                    return false;
                }

                index += 2;

                continue;
            }

            index++;
        }

        error = FormatError("Unterminated string literal.", openPosition);

        return false;
    }
}
