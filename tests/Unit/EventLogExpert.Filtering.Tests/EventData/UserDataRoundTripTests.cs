// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Structured;
using EventLogExpert.Filtering.Parsing;
using System.Text.Json;

namespace EventLogExpert.Filtering.Tests.EventData;

/// <summary>
///     Advanced-text to/from Basic round-trip fidelity and JSON persistence for UserData rows. The path-form
///     invariant: <see cref="FilterComparison.UserDataFieldName" /> holds the storage key, the formatter emits
///     <c>UserData["storage-key"]</c>, and re-parsing re-roots it, so a storage key survives format -> compile ->
///     decompose unchanged (element and attribute leaves).
/// </summary>
public sealed class UserDataRoundTripTests
{
    [Fact]
    public void EmptyFieldName_DoesNotFormat() =>
        Assert.False(BasicFilterFormatter.TryFormatComparison(Single(ComparisonOperator.Equals, "", "admin"), null, out _));

    [Fact]
    public void Format_EmitsUserDataIndexerExpression()
    {
        Assert.True(
            BasicFilterFormatter.TryFormat(
                new BasicFilter(Single(ComparisonOperator.Equals, "X509Objects/Certificate/SubjectName", "value"), []),
                out var text),
            "format failed");

        Assert.Equal("UserData[\"X509Objects/Certificate/SubjectName\"] == \"value\"", text);
    }

    [Fact]
    public void Json_NonUserDataRow_OmitsUserDataFieldName_EvenWhenSet()
    {
        var comparison = new FilterComparison
        {
            Property = EventProperty.Source,
            Operator = ComparisonOperator.Equals,
            MatchMode = MatchMode.Single,
            Value = "x",
            UserDataFieldName = "stale"
        };

        var json = JsonSerializer.Serialize(comparison);

        Assert.DoesNotContain("UserDataFieldName", json);
    }

    [Fact]
    public void Json_PreservesUserDataFieldName()
    {
        var original = Single(ComparisonOperator.Contains, "X509Objects/Certificate/SubjectName", "admin");

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<FilterComparison>(json);

        Assert.NotNull(restored);
        Assert.Equal(EventProperty.UserData, restored!.Property);
        Assert.Equal("X509Objects/Certificate/SubjectName", restored.UserDataFieldName);
        Assert.Equal(ComparisonOperator.Contains, restored.Operator);
        Assert.Equal("admin", restored.Value);
    }

    [Fact]
    public void Json_Read_DropsUserDataFieldName_ForNonUserDataRow()
    {
        const string json =
            """{"Property":"Source","Operator":"Equals","MatchMode":"Single","Value":"x","UserDataFieldName":"stale"}""";

        var restored = JsonSerializer.Deserialize<FilterComparison>(json);

        Assert.NotNull(restored);
        Assert.Equal(EventProperty.Source, restored!.Property);
        Assert.Null(restored.UserDataFieldName);
    }

    [Fact]
    public void Json_Read_NormalizesWhitespaceFieldName_ToNull()
    {
        const string json =
            """{"Property":"UserData","Operator":"Equals","MatchMode":"Single","Value":"x","UserDataFieldName":"   "}""";

        var restored = JsonSerializer.Deserialize<FilterComparison>(json);

        Assert.NotNull(restored);
        Assert.Equal(EventProperty.UserData, restored!.Property);
        Assert.Null(restored.UserDataFieldName);
    }

    [Fact]
    public void ManyValues_RoundTrips()
    {
        var original = new FilterComparison
        {
            Property = EventProperty.UserData,
            UserDataFieldName = "X509Objects/Certificate/SubjectName",
            Operator = ComparisonOperator.Equals,
            MatchMode = MatchMode.Many,
            Values = ["admin", "root"]
        };

        var result = RoundTrip(original);

        Assert.Equal(EventProperty.UserData, result.Property);
        Assert.Equal("X509Objects/Certificate/SubjectName", result.UserDataFieldName);
        Assert.Equal(MatchMode.Many, result.MatchMode);
        Assert.Equal<IEnumerable<string>>(["admin", "root"], result.Values);
    }

    [Fact]
    public void NegatedContains_MapsToNotContains()
    {
        Assert.True(
            BasicFilterDecomposer.TryDecompose(
                "!UserData[\"X509Objects/Certificate/SubjectName\"].Contains(\"adm\", StringComparison.OrdinalIgnoreCase)",
                out var structured),
            "decompose failed");

        Assert.Equal(ComparisonOperator.NotContains, structured!.Comparison.Operator);
        Assert.Equal("X509Objects/Certificate/SubjectName", structured.Comparison.UserDataFieldName);
    }

    [Fact]
    public void NegatedNotEqual_CanonicalizesToEquals()
    {
        Assert.True(
            BasicFilterDecomposer.TryDecompose(
                "!(UserData[\"X509Objects/Certificate/SubjectName\"] != \"admin\")",
                out var structured),
            "decompose failed");

        Assert.Equal(ComparisonOperator.Equals, structured!.Comparison.Operator);
        Assert.Equal("X509Objects/Certificate/SubjectName", structured.Comparison.UserDataFieldName);
    }

    [Theory]
    [InlineData(ComparisonOperator.Equals)]
    [InlineData(ComparisonOperator.NotEqual)]
    [InlineData(ComparisonOperator.Contains)]
    [InlineData(ComparisonOperator.NotContains)]
    public void SingleValue_RoundTrips(ComparisonOperator op) =>
        AssertSingleRoundTrips(op, "X509Objects/Certificate/SubjectName", "admin");

    // Core invariant: a storage key survives the full Basic -> Advanced-text -> compile -> decompose loop unchanged, for
    // both element and attribute (@name) leaves, so the value the field picker shows equals the stored field's key.
    [Theory]
    [InlineData("Foo")]
    [InlineData("Foo/Bar")]
    [InlineData("X509Objects/Certificate/SubjectName")]
    [InlineData("X509Objects/Certificate/@subjectName")]
    [InlineData("*cert*")]
    [InlineData("X509Objects/*/@subjectName")]
    public void StorageKey_RoundTripsThroughCanonicalAndCompiles(string storageKey)
    {
        // Direct invariant: rooting then re-keying is identity on a storage key.
        Assert.True(UserDataFieldPath.TryNormalize(storageKey, out var canonical, out var normalizeError), normalizeError);
        Assert.Equal(storageKey, UserDataFieldPath.ToStorageKey(canonical!));

        // Text invariant: the formatted row is valid grammar (compiles) and decomposes back to the same storage key.
        Assert.True(
            BasicFilterFormatter.TryFormat(
                new BasicFilter(Single(ComparisonOperator.Equals, storageKey, "value"), []),
                out var text),
            "format failed");

        Assert.True(FilterParser.TryCompile(text, out var compiled, out var compileError), compileError);
        Assert.NotNull(compiled.Evaluate);

        Assert.True(BasicFilterDecomposer.TryDecompose(text, out var structured), $"decompose failed: {text}");
        Assert.Equal(EventProperty.UserData, structured!.Comparison.Property);
        Assert.Equal(storageKey, structured.Comparison.UserDataFieldName);
    }

    [Fact]
    public void StoredGrammarText_DecomposesFormatsToSameText()
    {
        const string original = "UserData[\"X509Objects/Certificate/SubjectName\"] == \"value\"";

        Assert.True(BasicFilterDecomposer.TryDecompose(original, out var structured), "decompose failed");
        Assert.True(BasicFilterFormatter.TryFormat(new BasicFilter(structured!.Comparison, []), out var text), "format failed");

        Assert.Equal(original, text);
    }

    private static void AssertSingleRoundTrips(ComparisonOperator op, string fieldName, string value)
    {
        var result = RoundTrip(Single(op, fieldName, value));

        Assert.Equal(EventProperty.UserData, result.Property);
        Assert.Equal(fieldName, result.UserDataFieldName);
        Assert.Equal(op, result.Operator);
        Assert.Equal(MatchMode.Single, result.MatchMode);
        Assert.Equal(value, result.Value);
    }

    private static FilterComparison RoundTrip(FilterComparison original)
    {
        Assert.True(BasicFilterFormatter.TryFormat(new BasicFilter(original, []), out var text), "format failed");
        Assert.True(BasicFilterDecomposer.TryDecompose(text, out var structured), $"decompose failed: {text}");

        return structured!.Comparison;
    }

    private static FilterComparison Single(ComparisonOperator op, string fieldName, string value) =>
        new()
        {
            Property = EventProperty.UserData,
            UserDataFieldName = fieldName,
            Operator = op,
            MatchMode = MatchMode.Single,
            Value = value
        };
}
