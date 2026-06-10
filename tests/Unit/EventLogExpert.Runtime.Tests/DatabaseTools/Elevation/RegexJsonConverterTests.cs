// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Ipc;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EventLogExpert.Runtime.Tests.DatabaseTools.Elevation;

public sealed class RegexJsonConverterTests
{
    [Fact]
    public void JsonObject_MissingPatternProperty_ThrowsJsonException()
    {
        const string Malformed = """{"options":1,"matchTimeoutMs":null}""";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Regex>(Malformed, DatabaseToolsIpcSerializer.Options));
    }

    [Fact]
    public void NullRegex_SerializesToJsonNull_AndDeserializesBackToNull()
    {
        var json = JsonSerializer.Serialize<Regex?>(null, DatabaseToolsIpcSerializer.Options);

        Assert.Equal("null", json);

        var roundTripped = JsonSerializer.Deserialize<Regex?>(json, DatabaseToolsIpcSerializer.Options);
        Assert.Null(roundTripped);
    }

    [Fact]
    public void Read_WithExcessiveTimeoutMs_ThrowsJsonException()
    {
        const string Json = """{"pattern":"abc","options":0,"matchTimeoutMs":10000}""";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Regex>(Json, DatabaseToolsIpcSerializer.Options));
    }

    [Fact]
    public void Read_WithInvalidOptionCombination_ThrowsJsonException()
    {
        // ECMAScript is incompatible with RightToLeft per Regex constructor docs.
        int incompatibleOptions = (int)(RegexOptions.ECMAScript | RegexOptions.RightToLeft);
        string json = $$"""{"pattern":"abc","options":{{incompatibleOptions}},"matchTimeoutMs":100}""";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Regex>(json, DatabaseToolsIpcSerializer.Options));
    }

    [Fact]
    public void Read_WithInvalidPattern_ThrowsJsonException()
    {
        // Unmatched '(' is a malformed regex pattern.
        const string Json = """{"pattern":"(","options":0,"matchTimeoutMs":100}""";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Regex>(Json, DatabaseToolsIpcSerializer.Options));
    }

    [Fact]
    public void Read_WithNegativeTimeoutMs_ThrowsJsonException()
    {
        const string Json = """{"pattern":"abc","options":0,"matchTimeoutMs":-5}""";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Regex>(Json, DatabaseToolsIpcSerializer.Options));
    }

    [Fact]
    public void Read_WithNullTimeoutMs_UsesDefaultTimeoutNotInfinite()
    {
        const string Json = """{"pattern":"abc","options":0,"matchTimeoutMs":null}""";

        var result = JsonSerializer.Deserialize<Regex>(Json, DatabaseToolsIpcSerializer.Options);

        Assert.NotNull(result);
        Assert.Equal(TimeSpan.FromMilliseconds(1000), result.MatchTimeout);
        Assert.NotEqual(Regex.InfiniteMatchTimeout, result.MatchTimeout);
    }

    [Fact]
    public void Read_WithUnknownOptionsBit_ThrowsJsonException()
    {
        // 0x80000000 is outside the AllowedOptions mask (no defined RegexOptions flag uses this bit).
        const string Json = """{"pattern":"abc","options":-2147483648,"matchTimeoutMs":100}""";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Regex>(Json, DatabaseToolsIpcSerializer.Options));
    }

    [Fact]
    public void Read_WithZeroTimeoutMs_ThrowsJsonException()
    {
        const string Json = """{"pattern":"abc","options":0,"matchTimeoutMs":0}""";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Regex>(Json, DatabaseToolsIpcSerializer.Options));
    }

    [Fact]
    public void Regex_WithFiniteMatchTimeout_EmitsMillisecondsAndRoundTripsTimeoutValue()
    {
        var original = new Regex(@"\d+", RegexOptions.None, TimeSpan.FromMilliseconds(1234));

        var json = JsonSerializer.Serialize(original, DatabaseToolsIpcSerializer.Options);

        using (var doc = JsonDocument.Parse(json))
        {
            Assert.Equal(1234, doc.RootElement.GetProperty("matchTimeoutMs").GetInt32());
        }

        var roundTripped = JsonSerializer.Deserialize<Regex>(json, DatabaseToolsIpcSerializer.Options);
        Assert.NotNull(roundTripped);
        Assert.Equal(TimeSpan.FromMilliseconds(1234), roundTripped.MatchTimeout);
    }

    [Fact]
    public void Regex_WithInfiniteMatchTimeout_WriterEmitsNullAndReaderMapsToDefaultTimeout()
    {
        var original = new Regex("hello");

        var json = JsonSerializer.Serialize(original, DatabaseToolsIpcSerializer.Options);

        using (var doc = JsonDocument.Parse(json))
        {
            Assert.Equal("hello", doc.RootElement.GetProperty("pattern").GetString());
            Assert.Equal(0, doc.RootElement.GetProperty("options").GetInt32());
            Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("matchTimeoutMs").ValueKind);
        }

        var roundTripped = JsonSerializer.Deserialize<Regex>(json, DatabaseToolsIpcSerializer.Options);
        Assert.NotNull(roundTripped);
        Assert.Equal("hello", roundTripped.ToString());
        Assert.Equal(RegexOptions.None, roundTripped.Options);
        Assert.Equal(TimeSpan.FromMilliseconds(1000), roundTripped.MatchTimeout);
    }

    [Fact]
    public void Regex_WithNonDefaultOptions_RoundTripsOptionsFlagsExactly()
    {
        var flags = RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant;
        var original = new Regex("^abc$", flags);

        var json = JsonSerializer.Serialize(original, DatabaseToolsIpcSerializer.Options);

        using (var doc = JsonDocument.Parse(json))
        {
            Assert.Equal((int)flags, doc.RootElement.GetProperty("options").GetInt32());
        }

        var roundTripped = JsonSerializer.Deserialize<Regex>(json, DatabaseToolsIpcSerializer.Options);
        Assert.NotNull(roundTripped);
        Assert.Equal(flags, roundTripped.Options);
    }

    [Fact]
    public void RoundTrippedRegex_MatchesSameInputsAsOriginal_ForCaseInsensitivePattern()
    {
        var original = new Regex("^foo$", RegexOptions.IgnoreCase);

        var json = JsonSerializer.Serialize(original, DatabaseToolsIpcSerializer.Options);
        var roundTripped = JsonSerializer.Deserialize<Regex>(json, DatabaseToolsIpcSerializer.Options);

        Assert.NotNull(roundTripped);
        Assert.Matches(roundTripped, "FOO");
        Assert.Matches(roundTripped, "foo");
        Assert.DoesNotMatch(roundTripped, "foobar");
    }
}
