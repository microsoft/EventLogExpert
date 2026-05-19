// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Parsing;

namespace EventLogExpert.Filtering.Tests.Parsing;

/// <summary>
///     Direct <see cref="Tokenizer" /> tests for boundary cases the public <see cref="FilterParser.TryValidate" />
///     suite cannot easily reach: error-message positions, the exact <see cref="ParseLimits.MaxStringLiteralLength" />
///     ceiling, and the position offsets diagnostics depend on.
/// </summary>
public sealed class TokenizerTests
{
    [Fact]
    public void TryTokenize_WhenAmpersandIsLone_ReturnsErrorMentioningPosition()
    {
        var ok = Tokenizer.TryTokenize("Id == 1 & 2", out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("position 8", error);
    }

    [Fact]
    public void TryTokenize_WhenBangPrefixesIdentifier_EmitsBangThenIdentifier()
    {
        var ok = Tokenizer.TryTokenize("!Source", out var tokens, out var error);

        Assert.True(ok, error);
        Assert.Equal(TokenKind.Bang, tokens[0].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[1].Kind);
        Assert.Equal("Source", tokens[1].Text);
    }

    [Fact]
    public void TryTokenize_WhenCompoundOperatorsAdjoinIdentifiers_RecordsPositionAtFirstChar()
    {
        var ok = Tokenizer.TryTokenize("Id==100", out var tokens, out var error);

        Assert.True(ok, error);
        Assert.Equal(TokenKind.Identifier, tokens[0].Kind);
        Assert.Equal(0, tokens[0].Position);
        Assert.Equal(TokenKind.EqEq, tokens[1].Kind);
        Assert.Equal(2, tokens[1].Position);
        Assert.Equal(TokenKind.Int, tokens[2].Kind);
        Assert.Equal(4, tokens[2].Position);
    }

    [Fact]
    public void TryTokenize_WhenIdentifierStartsWithUnderscore_AcceptsAsIdentifier()
    {
        var ok = Tokenizer.TryTokenize("_field == 1", out var tokens, out var error);

        Assert.True(ok, error);
        Assert.Equal(TokenKind.Identifier, tokens[0].Kind);
        Assert.Equal("_field", tokens[0].Text);
    }

    [Fact]
    public void TryTokenize_WhenInputEndsCleanly_AppendsEndToken()
    {
        var ok = Tokenizer.TryTokenize("Id", out var tokens, out var error);

        Assert.True(ok, error);
        Assert.Equal(TokenKind.End, tokens[^1].Kind);
        Assert.Equal(2, tokens[^1].Position);
    }

    [Fact]
    public void TryTokenize_WhenInputIsLongerThanMaxLength_ReturnsErrorBeforeScanning()
    {
        var oversized = new string('x', ParseLimits.MaxInputLength + 1);

        var ok = Tokenizer.TryTokenize(oversized, out var tokens, out var error);

        Assert.False(ok);
        Assert.Empty(tokens);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryTokenize_WhenKeywordsNewAndNullAppear_AssignsDistinctTokenKinds()
    {
        var ok = Tokenizer.TryTokenize("new null", out var tokens, out var error);

        Assert.True(ok, error);
        Assert.Equal(TokenKind.New, tokens[0].Kind);
        Assert.Equal(TokenKind.Null, tokens[1].Kind);
    }

    [Fact]
    public void TryTokenize_WhenPipeIsLone_ReturnsErrorMentioningPosition()
    {
        var ok = Tokenizer.TryTokenize("Id == 1 | 2", out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("position 8", error);
    }

    [Fact]
    public void TryTokenize_WhenSourceIsNull_ReturnsErrorAndEmptyTokens()
    {
        var ok = Tokenizer.TryTokenize(null!, out var tokens, out var error);

        Assert.False(ok);
        Assert.Empty(tokens);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("\"\\\\\"", "\\")]
    [InlineData("\"\\\"\"", "\"")]
    [InlineData("\"\\r\"", "\r")]
    [InlineData("\"\\n\"", "\n")]
    [InlineData("\"\\t\"", "\t")]
    [InlineData("\"plain\"", "plain")]
    [InlineData("\"a\\\\b\\nc\"", "a\\b\nc")]
    public void TryTokenize_WhenStringLiteralContainsKnownEscape_DecodesToTargetCharacter(string source, string expected)
    {
        var ok = Tokenizer.TryTokenize(source, out var tokens, out var error);

        Assert.True(ok, error);
        Assert.Equal(TokenKind.String, tokens[0].Kind);
        Assert.Equal(expected, tokens[0].Text);
    }

    [Fact]
    public void TryTokenize_WhenStringLiteralContainsUnknownEscape_ReturnsErrorWithOpeningQuotePosition()
    {
        var ok = Tokenizer.TryTokenize("\"a\\xb\"", out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("position 0", error);
    }

    [Fact]
    public void TryTokenize_WhenStringLiteralExceedsCeiling_ReturnsErrorWithOpeningQuotePosition()
    {
        var oversized = new string('a', ParseLimits.MaxStringLiteralLength + 1);
        var input = "Source == \"" + oversized + "\"";

        var ok = Tokenizer.TryTokenize(input, out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("position 10", error);
    }

    [Fact]
    public void TryTokenize_WhenStringLiteralIsUnterminated_ReturnsErrorAtOpeningQuote()
    {
        var ok = Tokenizer.TryTokenize("Source == \"abc", out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("position 10", error);
    }

    [Fact]
    public void TryTokenize_WhenTrailingBackslashStartsEscape_ReturnsErrorAtOpeningQuote()
    {
        var ok = Tokenizer.TryTokenize("Source == \"abc\\", out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("position 10", error);
    }
}
