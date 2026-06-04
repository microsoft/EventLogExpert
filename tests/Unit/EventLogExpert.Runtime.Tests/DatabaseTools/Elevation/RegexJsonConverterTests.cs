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
    public void Regex_WithDefaultsAndInfiniteMatchTimeout_EmitsNullTimeoutAndRoundTripsInfinite()
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
        Assert.Equal(Regex.InfiniteMatchTimeout, roundTripped.MatchTimeout);
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
