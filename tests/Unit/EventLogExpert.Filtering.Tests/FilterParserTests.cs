// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Filtering.Tests;

public sealed class FilterParserTests
{
    [Theory]
    // Bare equality on string properties (formatter and Advanced)
    [InlineData("Source == \"TestSource\"")]
    [InlineData("ComputerName == \"SERVER01\"")]
    [InlineData("Level == \"Error\"")]
    [InlineData("LogName == \"Application\"")]
    [InlineData("TaskCategory == \"System\"")]
    [InlineData("Description == \"An error occurred\"")]
    [InlineData("Xml == \"<x/>\"")]
    // Bare inequality
    [InlineData("Source != \"TestSource\"")]
    // Numeric properties (formatter emits string-quoted; Dynamic.Core coerces)
    [InlineData("Id == \"100\"")]
    [InlineData("Id == 100")]
    [InlineData("Id != 100")]
    [InlineData("Id > 100")]
    [InlineData("Id < 100")]
    [InlineData("Id >= 100")]
    [InlineData("Id <= 100")]
    [InlineData("ProcessId == 4")]
    [InlineData("ThreadId == 8")]
    [InlineData("RecordId == 1234567890123")]
    [InlineData("ActivityId == \"00000000-0000-0000-0000-000000000000\"")]
    // Contains shapes (formatter always emits OIC; Advanced may omit)
    [InlineData("Source.Contains(\"Test\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData("Source.Contains(\"Test\")")]
    [InlineData("Description.Contains(\"error occurred\")")]
    [InlineData("TaskCategory.Contains(\"Security\")")]
    [InlineData("xml.Contains(\"data\")")]
    [InlineData("Id.ToString().Contains(\"1\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData("ActivityId.ToString().Contains(\"abc\", StringComparison.OrdinalIgnoreCase)")]
    // Boolean composition
    [InlineData("Id == 100 && Level == \"Error\"")]
    [InlineData("Id == 100 || Id == 200")]
    [InlineData("(Id == 100 || Id == 200) && Level == \"Error\"")]
    [InlineData("!(Source == \"X\")")]
    [InlineData("Source == \"A\" || Source == \"B\" || Source == \"C\"")]
    // Multi-equals (Many) shapes from formatter
    [InlineData("(new[] {\"100\", \"200\"}).Contains(Id.ToString())")]
    [InlineData("(new[] {\"Error\", \"Warning\"}).Contains(Level.ToString())")]
    [InlineData("(new[] {\"A\", \"B\"}).Contains(Source)")]
    [InlineData("(new[] {\"X\"}).Contains(ComputerName)")]
    // Keywords.Any shapes from formatter
    [InlineData("Keywords.Any(e => string.Equals(e, \"audit\", StringComparison.OrdinalIgnoreCase))")]
    [InlineData("Keywords.Any(e => e.Contains(\"audit\", StringComparison.OrdinalIgnoreCase))")]
    [InlineData("Keywords.Any(e => (new[] {\"audit\", \"system\"}).Contains(e))")]
    [InlineData("!Keywords.Any(e => string.Equals(e, \"audit\", StringComparison.OrdinalIgnoreCase))")]
    [InlineData("!Keywords.Any(e => e.Contains(\"audit\", StringComparison.OrdinalIgnoreCase))")]
    // UserId 4-shape formatter output
    [InlineData("UserId != null && UserId.Value == \"S-1-5-18\"")]
    [InlineData("UserId != null && UserId.Value != \"S-1-5-18\"")]
    [InlineData("UserId != null && UserId.Value.Contains(\"5-18\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData("UserId != null && !UserId.Value.Contains(\"5-18\", StringComparison.OrdinalIgnoreCase)")]
    // Identifier case-insensitivity (parity with Dynamic.Core)
    [InlineData("source == \"X\"")]
    [InlineData("SOURCE == \"X\"")]
    // Escape sequences in string literals
    [InlineData("Description == \"line1\\nline2\"")]
    [InlineData("Description == \"tab\\there\"")]
    [InlineData("Description == \"a\\\\b\"")]
    [InlineData("Description == \"quote\\\"inside\"")]
    public void TryValidate_AcceptsClosedGrammar(string filter)
    {
        var ok = FilterParser.TryValidate(filter, out var error);

        Assert.True(ok, $"Expected '{filter}' to validate; got error: {error}");
        Assert.Null(error);
    }

    [Fact]
    public void TryValidate_HandlesMultilineFormatterOutput()
    {
        // BasicFilterFormatter emits sub-filters separated by AppendLine, producing CRLF between conditions.
        // The tokenizer treats CRLF as whitespace.
        var filter = "Id == 100\r\n && Level == \"Error\"";

        var ok = FilterParser.TryValidate(filter, out var error);

        Assert.True(ok, error);
    }

    [Fact]
    public void TryValidate_PrecedenceMatchesCSharp()
    {
        // && binds tighter than ||, so this is (A) || (B && C) — must validate as one big OR.
        var filter = "Id == 1 || Id == 2 && Level == \"Error\"";

        var ok = FilterParser.TryValidate(filter, out var error);

        Assert.True(ok, error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void TryValidate_RejectsEmpty(string? filter)
    {
        var ok = FilterParser.TryValidate(filter, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryValidate_RejectsExcessiveArrayLiteralLength()
    {
        // 257 array elements exceeds MaxArrayElements=256.
        var elements = string.Join(", ", Enumerable.Range(0, 257).Select(i => $"\"{i}\""));
        var filter = $"(new[] {{{elements}}}).Contains(Source)";

        var ok = FilterParser.TryValidate(filter, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryValidate_RejectsExcessiveParseDepth()
    {
        // 64 nested parens around a trivial comparison exceeds MaxParseDepth=32.
        var depth = 64;
        var filter = new string('(', depth) + "Id == 1" + new string(')', depth);

        var ok = FilterParser.TryValidate(filter, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryValidate_RejectsInputBeyondMaxLength()
    {
        var oversized = new string('a', 4097);
        var filter = $"Source == \"{oversized}\"";

        var ok = FilterParser.TryValidate(filter, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryValidate_RejectsUnknownEscapeSequence()
    {
        var filter = "Source == \"\\u0041\"";

        var ok = FilterParser.TryValidate(filter, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Theory]
    // Unknown property
    [InlineData("InvalidProperty == 100")]
    // RHS not a literal
    [InlineData("Id == invalid")]
    [InlineData("Source == Description")]
    // Numeric coercion failure
    [InlineData("Id == \"not-a-number\"")]
    [InlineData("ActivityId == \"not-a-guid\"")]
    [InlineData("RecordId == \"abc\"")]
    // Unsupported method on string property
    [InlineData("Source.StartsWith(\"X\")")]
    [InlineData("Source.EndsWith(\"X\")")]
    [InlineData("Source.IndexOf(\"X\") > 0")]
    // Wrong StringComparison value
    [InlineData("Source.Contains(\"x\", StringComparison.Ordinal)")]
    [InlineData("Source.Contains(\"x\", StringComparison.CurrentCulture)")]
    // Keywords.Any with unsupported lambda body
    [InlineData("Keywords.Any(e => e.StartsWith(\"X\"))")]
    [InlineData("Keywords.Any(e => e == \"X\")")]
    // Bare property reference (no operator)
    [InlineData("Source")]
    [InlineData("Id")]
    // Mismatched parens / syntax errors
    [InlineData("(Id == 100")]
    [InlineData("Id == 100 &&")]
    [InlineData("&& Id == 100")]
    [InlineData("Id ===  100")]
    // Empty array
    [InlineData("(new[] {}).Contains(Source)")]
    // Property as RHS of comparison
    [InlineData("\"X\" == Source")]
    public void TryValidate_RejectsUnsupportedShapes(string filter)
    {
        var ok = FilterParser.TryValidate(filter, out var error);

        Assert.False(ok, $"Expected '{filter}' to be rejected, but it validated.");
        Assert.NotNull(error);
    }

    [Fact]
    public void TryValidate_RejectsUnterminatedString()
    {
        var filter = "Source == \"unterminated";

        var ok = FilterParser.TryValidate(filter, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }
}
