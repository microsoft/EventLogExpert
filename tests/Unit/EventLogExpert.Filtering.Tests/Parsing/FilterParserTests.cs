// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Parsing;

namespace EventLogExpert.Filtering.Tests.Parsing;

public sealed class FilterParserTests
{
    [Theory]
    [InlineData("Source == \"TestSource\"")]
    [InlineData("ComputerName == \"SERVER01\"")]
    [InlineData("Level == \"Error\"")]
    [InlineData("LogName == \"Application\"")]
    [InlineData("TaskCategory == \"System\"")]
    [InlineData("Description == \"An error occurred\"")]
    [InlineData("Xml == \"<x/>\"")]
    [InlineData("Source != \"TestSource\"")]
    // Numeric properties — string-quoted RHS coerces to int at lower time.
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
    // Contains — both default and OrdinalIgnoreCase argument shapes are accepted.
    [InlineData("Source.Contains(\"Test\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData("Source.Contains(\"Test\")")]
    [InlineData("Description.Contains(\"error occurred\")")]
    [InlineData("TaskCategory.Contains(\"Security\")")]
    [InlineData("xml.Contains(\"data\")")]
    [InlineData("Id.ToString().Contains(\"1\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData("ActivityId.ToString().Contains(\"abc\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData("Id == 100 && Level == \"Error\"")]
    [InlineData("Id == 100 || Id == 200")]
    [InlineData("(Id == 100 || Id == 200) && Level == \"Error\"")]
    [InlineData("!(Source == \"X\")")]
    [InlineData("Source == \"A\" || Source == \"B\" || Source == \"C\"")]
    [InlineData("(new[] {\"100\", \"200\"}).Contains(Id.ToString())")]
    [InlineData("(new[] {\"Error\", \"Warning\"}).Contains(Level.ToString())")]
    [InlineData("(new[] {\"A\", \"B\"}).Contains(Source)")]
    [InlineData("(new[] {\"X\"}).Contains(ComputerName)")]
    [InlineData("Keywords.Any(e => string.Equals(e, \"audit\", StringComparison.OrdinalIgnoreCase))")]
    [InlineData("Keywords.Any(e => e.Contains(\"audit\", StringComparison.OrdinalIgnoreCase))")]
    [InlineData("Keywords.Any(e => (new[] {\"audit\", \"system\"}).Contains(e))")]
    [InlineData("!Keywords.Any(e => string.Equals(e, \"audit\", StringComparison.OrdinalIgnoreCase))")]
    [InlineData("!Keywords.Any(e => e.Contains(\"audit\", StringComparison.OrdinalIgnoreCase))")]
    // UserId — the formatter emits 4 distinct guarded shapes the parser must accept.
    [InlineData("UserId != null && UserId.Value == \"S-1-5-18\"")]
    [InlineData("UserId != null && UserId.Value != \"S-1-5-18\"")]
    [InlineData("UserId != null && UserId.Value.Contains(\"5-18\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData("UserId != null && !UserId.Value.Contains(\"5-18\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData("source == \"X\"")]
    [InlineData("SOURCE == \"X\"")]
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
        // Legacy on-disk Basic filters used CRLF between sub-conditions; CRLF is treated as whitespace.
        var filter = "Id == 100\r\n && Level == \"Error\"";

        var ok = FilterParser.TryValidate(filter, out var error);

        Assert.True(ok, error);
    }

    [Fact]
    public void TryValidate_PrecedenceMatchesCSharp()
    {
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
        // 257 elements exceeds ParseLimits.MaxArrayElements (256).
        var elements = string.Join(", ", Enumerable.Range(0, 257).Select(i => $"\"{i}\""));
        var filter = $"(new[] {{{elements}}}).Contains(Source)";

        var ok = FilterParser.TryValidate(filter, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryValidate_RejectsExcessiveParseDepth()
    {
        // 64 nested parens exceeds ParseLimits.MaxParseDepth (32).
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
    [InlineData("InvalidProperty == 100")]
    [InlineData("Id == invalid")]
    [InlineData("Source == Description")]
    [InlineData("Id == \"not-a-number\"")]
    [InlineData("ActivityId == \"not-a-guid\"")]
    [InlineData("RecordId == \"abc\"")]
    [InlineData("Source.StartsWith(\"X\")")]
    [InlineData("Source.EndsWith(\"X\")")]
    [InlineData("Source.IndexOf(\"X\") > 0")]
    [InlineData("Source.Contains(\"x\", StringComparison.Ordinal)")]
    [InlineData("Source.Contains(\"x\", StringComparison.CurrentCulture)")]
    [InlineData("Keywords.Any(e => e.StartsWith(\"X\"))")]
    [InlineData("Keywords.Any(e => e == \"X\")")]
    [InlineData("Source")]
    [InlineData("Id")]
    [InlineData("(Id == 100")]
    [InlineData("Id == 100 &&")]
    [InlineData("&& Id == 100")]
    [InlineData("Id ===  100")]
    [InlineData("(new[] {}).Contains(Source)")]
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
