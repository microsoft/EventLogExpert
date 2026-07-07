// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.Json;

namespace EventLogExpert.Filtering.Tests.EventData;

/// <summary>
///     Advanced-text ↔ Basic round-trip fidelity and JSON persistence for EventData rows, including the negation
///     canonicalizations (<c>!(!=)</c> → <c>==</c>, <c>!.Contains</c> → NotContains) and field-name escaping.
/// </summary>
public sealed class EventDataRoundTripTests
{
    [Fact]
    public void EmptyFieldName_DoesNotFormat() =>
        Assert.False(BasicFilterFormatter.TryFormatComparison(Single(ComparisonOperator.Equals, "", "admin"), null, out _));

    [Theory]
    [InlineData("with space")]
    [InlineData("with\"quote")]
    [InlineData("with]bracket")]
    [InlineData("with\\backslash")]
    [InlineData("with\ttab")]
    [InlineData("*cert*")]
    public void FieldNameWithSpecialCharacters_RoundTrips(string fieldName) =>
        AssertSingleRoundTrips(ComparisonOperator.Equals, fieldName, "value");

    [Fact]
    public void Json_NonEventDataRow_OmitsEventDataFieldName_EvenWhenSet()
    {
        var comparison = new FilterComparison
        {
            Property = EventProperty.Source,
            Operator = ComparisonOperator.Equals,
            MatchMode = MatchMode.Single,
            Value = "x",
            EventDataFieldName = "stale"
        };

        var json = JsonSerializer.Serialize(comparison);

        Assert.DoesNotContain("EventDataFieldName", json);
    }

    [Fact]
    public void Json_PreservesEventDataFieldName()
    {
        var original = Single(ComparisonOperator.Contains, "TargetUserName", "admin");

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<FilterComparison>(json);

        Assert.NotNull(restored);
        Assert.Equal(EventProperty.EventData, restored!.Property);
        Assert.Equal("TargetUserName", restored.EventDataFieldName);
        Assert.Equal(ComparisonOperator.Contains, restored.Operator);
        Assert.Equal("admin", restored.Value);
    }

    [Fact]
    public void Json_Read_DropsEventDataFieldName_ForNonEventDataRow()
    {
        const string json =
            """{"Property":"Source","Operator":"Equals","MatchMode":"Single","Value":"x","EventDataFieldName":"stale"}""";

        var restored = JsonSerializer.Deserialize<FilterComparison>(json);

        Assert.NotNull(restored);
        Assert.Equal(EventProperty.Source, restored!.Property);
        Assert.Null(restored.EventDataFieldName);
    }

    [Fact]
    public void Json_Read_NormalizesWhitespaceFieldName_ToNull()
    {
        const string json =
            """{"Property":"EventData","Operator":"Equals","MatchMode":"Single","Value":"x","EventDataFieldName":"   "}""";

        var restored = JsonSerializer.Deserialize<FilterComparison>(json);

        Assert.NotNull(restored);
        Assert.Equal(EventProperty.EventData, restored!.Property);
        Assert.Null(restored.EventDataFieldName);
    }

    [Fact]
    public void ManyValues_RoundTrips()
    {
        var original = new FilterComparison
        {
            Property = EventProperty.EventData,
            EventDataFieldName = "User",
            Operator = ComparisonOperator.Equals,
            MatchMode = MatchMode.Many,
            Values = ["admin", "root"]
        };

        var result = RoundTrip(original);

        Assert.Equal(EventProperty.EventData, result.Property);
        Assert.Equal("User", result.EventDataFieldName);
        Assert.Equal(MatchMode.Many, result.MatchMode);
        Assert.Equal<IEnumerable<string>>(["admin", "root"], result.Values);
    }

    [Fact]
    public void NegatedContains_MapsToNotContains()
    {
        Assert.True(
            BasicFilterDecomposer.TryDecompose(
                "!EventData[\"User\"].Contains(\"adm\", StringComparison.OrdinalIgnoreCase)",
                out var structured),
            "decompose failed");

        Assert.Equal(ComparisonOperator.NotContains, structured!.Comparison.Operator);
        Assert.Equal("User", structured.Comparison.EventDataFieldName);
    }

    [Fact]
    public void NegatedNotEqual_CanonicalizesToEquals()
    {
        Assert.True(
            BasicFilterDecomposer.TryDecompose("!(EventData[\"User\"] != \"admin\")", out var structured),
            "decompose failed");

        Assert.Equal(ComparisonOperator.Equals, structured!.Comparison.Operator);
        Assert.Equal("User", structured.Comparison.EventDataFieldName);
    }

    [Theory]
    [InlineData(ComparisonOperator.Equals)]
    [InlineData(ComparisonOperator.NotEqual)]
    [InlineData(ComparisonOperator.Contains)]
    [InlineData(ComparisonOperator.NotContains)]
    public void SingleValue_RoundTrips(ComparisonOperator op) =>
        AssertSingleRoundTrips(op, "TargetUserName", "admin");

    private static void AssertSingleRoundTrips(ComparisonOperator op, string fieldName, string value)
    {
        var result = RoundTrip(Single(op, fieldName, value));

        Assert.Equal(EventProperty.EventData, result.Property);
        Assert.Equal(fieldName, result.EventDataFieldName);
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
            Property = EventProperty.EventData,
            EventDataFieldName = fieldName,
            Operator = op,
            MatchMode = MatchMode.Single,
            Value = value
        };
}
