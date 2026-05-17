// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Common;
using System.Collections.Immutable;
using System.Text.Json;

namespace EventLogExpert.Filtering.Tests;

public sealed class FilterComparisonJsonConverterTests
{
    [Fact]
    public void EventProperty_MemberCount_IsFrozenAcrossF16()
    {
        // Act + Assert — pairs with the per-ordinal theory above: a new EventProperty value or a removal would
        // leave the theory passing while still corrupting legacy reads for the missing ordinal. This guard forces
        // a deliberate Theory update.
        Assert.Equal(11, Enum.GetValues<EventProperty>().Length);
    }

    [Fact]
    public void JsonRoundTrip_ModernShape_PreservesAllFields()
    {
        // Arrange
        var original = new FilterComparison
        {
            Property = EventProperty.Source,
            Operator = ComparisonOperator.Contains,
            MatchMode = MatchMode.Many,
            Value = "abc",
            Values = ImmutableList.Create("a", "b", "c")
        };

        // Act
        string json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<FilterComparison>(json);

        // Assert
        Assert.NotNull(restored);
        Assert.Equal(EventProperty.Source, restored.Property);
        Assert.Equal(ComparisonOperator.Contains, restored.Operator);
        Assert.Equal(MatchMode.Many, restored.MatchMode);
        Assert.Equal("abc", restored.Value);
        Assert.Equal(["a", "b", "c"], restored.Values);
    }

    [Fact]
    public void Read_LegacyCategoryAndModernProperty_LastKeyInJsonWins()
    {
        // Arrange
        const string CategoryThenProperty =
            """{ "Category": 0, "Property": "Source", "Operator": "Equals", "MatchMode": "Single", "Values": [] }""";

        const string PropertyThenCategory =
            """{ "Property": "Source", "Category": 0, "Operator": "Equals", "MatchMode": "Single", "Values": [] }""";

        // Act
        var categoryThenProperty = JsonSerializer.Deserialize<FilterComparison>(CategoryThenProperty);
        var propertyThenCategory = JsonSerializer.Deserialize<FilterComparison>(PropertyThenCategory);

        // Assert
        Assert.NotNull(categoryThenProperty);
        Assert.NotNull(propertyThenCategory);
        Assert.Equal(EventProperty.Source, categoryThenProperty.Property);
        Assert.Equal(EventProperty.Id, propertyThenCategory.Property);
    }

    [Theory]
    [InlineData(0, EventProperty.Id)]
    [InlineData(1, EventProperty.ActivityId)]
    [InlineData(2, EventProperty.Level)]
    [InlineData(3, EventProperty.Keywords)]
    [InlineData(4, EventProperty.Source)]
    [InlineData(5, EventProperty.TaskCategory)]
    [InlineData(6, EventProperty.ProcessId)]
    [InlineData(7, EventProperty.ThreadId)]
    [InlineData(8, EventProperty.UserId)]
    [InlineData(9, EventProperty.Description)]
    [InlineData(10, EventProperty.Xml)]
    public void Read_LegacyCategoryKey_ReadsAsProperty(int legacyCategoryOrdinal, EventProperty expectedProperty)
    {
        // Arrange
        string json = $$"""{ "Category": {{legacyCategoryOrdinal}}, "Operator": "Equals", "MatchMode": "Single", "Value": "x", "Values": [] }""";

        // Act
        var restored = JsonSerializer.Deserialize<FilterComparison>(json);

        // Assert
        Assert.NotNull(restored);
        Assert.Equal(expectedProperty, restored.Property);
    }

    [Theory]
    [InlineData(0, ComparisonOperator.Equals, MatchMode.Single)]
    [InlineData(1, ComparisonOperator.Contains, MatchMode.Single)]
    [InlineData(2, ComparisonOperator.NotEqual, MatchMode.Single)]
    [InlineData(3, ComparisonOperator.NotContains, MatchMode.Single)]
    [InlineData(4, ComparisonOperator.Equals, MatchMode.Many)]
    public void Read_LegacyEvaluatorNumeric_MapsToOperatorAndMatchMode(
        int legacyEvaluator,
        ComparisonOperator expectedOperator,
        MatchMode expectedMatchMode)
    {
        // Arrange
        string json = $$"""{ "Property": "Id", "Evaluator": {{legacyEvaluator}}, "Value": "100", "Values": [] }""";

        // Act
        var restored = JsonSerializer.Deserialize<FilterComparison>(json);

        // Assert
        Assert.NotNull(restored);
        Assert.Equal(expectedOperator, restored.Operator);
        Assert.Equal(expectedMatchMode, restored.MatchMode);
    }

    [Theory]
    [InlineData("Equals", ComparisonOperator.Equals, MatchMode.Single)]
    [InlineData("Contains", ComparisonOperator.Contains, MatchMode.Single)]
    [InlineData("NotEqual", ComparisonOperator.NotEqual, MatchMode.Single)]
    [InlineData("Not Equal", ComparisonOperator.NotEqual, MatchMode.Single)]
    [InlineData("NotContains", ComparisonOperator.NotContains, MatchMode.Single)]
    [InlineData("Not Contains", ComparisonOperator.NotContains, MatchMode.Single)]
    [InlineData("MultiSelect", ComparisonOperator.Equals, MatchMode.Many)]
    [InlineData("Multi Select", ComparisonOperator.Equals, MatchMode.Many)]
    public void Read_LegacyEvaluatorString_MapsToOperatorAndMatchMode(
        string legacyEvaluator,
        ComparisonOperator expectedOperator,
        MatchMode expectedMatchMode)
    {
        // Arrange
        string json = $$"""{ "Property": "Id", "Evaluator": "{{legacyEvaluator}}", "Value": "100", "Values": [] }""";

        // Act
        var restored = JsonSerializer.Deserialize<FilterComparison>(json);

        // Assert
        Assert.NotNull(restored);
        Assert.Equal(expectedOperator, restored.Operator);
        Assert.Equal(expectedMatchMode, restored.MatchMode);
    }

    [Fact]
    public void Read_ModernMatchModeAndLegacyEvaluatorMultiSelect_ModernMatchModeWins()
    {
        // Arrange — Evaluator=MultiSelect would imply MatchMode.Many, but modern MatchMode=Single must take
        // precedence regardless of JSON key order (matchModeSet flag).
        const string ModernFirst =
            """{ "Property": "Id", "Operator": "Equals", "MatchMode": "Single", "Evaluator": "MultiSelect", "Values": [] }""";

        const string LegacyFirst =
            """{ "Property": "Id", "Evaluator": "MultiSelect", "Operator": "Equals", "MatchMode": "Single", "Values": [] }""";

        // Act
        var modernFirst = JsonSerializer.Deserialize<FilterComparison>(ModernFirst);
        var legacyFirst = JsonSerializer.Deserialize<FilterComparison>(LegacyFirst);

        // Assert
        Assert.NotNull(modernFirst);
        Assert.NotNull(legacyFirst);
        Assert.Equal(MatchMode.Single, modernFirst.MatchMode);
        Assert.Equal(MatchMode.Single, legacyFirst.MatchMode);
    }

    [Fact]
    public void Read_ModernOperatorAndLegacyEvaluator_ModernOperatorWins()
    {
        // Arrange — when both modern and legacy keys are present, the converter sets the flag on modern read so
        // legacy Evaluator does NOT overwrite it. Verified for Operator/MatchMode (operatorSet/matchModeSet flags).
        const string ModernFirst =
            """{ "Property": "Id", "Operator": "Contains", "MatchMode": "Single", "Evaluator": "Equals", "Values": [] }""";

        const string LegacyFirst =
            """{ "Property": "Id", "Evaluator": "Equals", "Operator": "Contains", "MatchMode": "Single", "Values": [] }""";

        // Act
        var modernFirst = JsonSerializer.Deserialize<FilterComparison>(ModernFirst);
        var legacyFirst = JsonSerializer.Deserialize<FilterComparison>(LegacyFirst);

        // Assert
        Assert.NotNull(modernFirst);
        Assert.NotNull(legacyFirst);
        Assert.Equal(ComparisonOperator.Contains, modernFirst.Operator);
        Assert.Equal(ComparisonOperator.Contains, legacyFirst.Operator);
    }

    [Fact]
    public void Read_UnknownProperty_IsSkipped()
    {
        // Arrange — guards against future field additions failing legacy reads. Unknown keys must be tolerated.
        const string JsonWithUnknown =
            """{ "Property": "Id", "Operator": "Equals", "MatchMode": "Single", "FutureField": { "nested": true }, "Value": "100", "Values": [] }""";

        // Act
        var restored = JsonSerializer.Deserialize<FilterComparison>(JsonWithUnknown);

        // Assert
        Assert.NotNull(restored);
        Assert.Equal(EventProperty.Id, restored.Property);
        Assert.Equal("100", restored.Value);
    }

    [Fact]
    public void Read_ValuesArray_HydratesImmutableList()
    {
        // Arrange
        const string Json =
            """{ "Property": "Source", "Operator": "Equals", "MatchMode": "Many", "Value": null, "Values": ["x", "y", "z"] }""";

        // Act
        var restored = JsonSerializer.Deserialize<FilterComparison>(Json);

        // Assert
        Assert.NotNull(restored);
        Assert.IsType<ImmutableList<string>>(restored.Values);
        Assert.Equal(["x", "y", "z"], restored.Values);
    }

    [Fact]
    public void Write_ModernShape_UsesStringEnumsAndOmitsLegacyKeys()
    {
        // Arrange
        var comparison = new FilterComparison
        {
            Property = EventProperty.Id,
            Operator = ComparisonOperator.Equals,
            MatchMode = MatchMode.Single,
            Value = "100",
            Values = []
        };

        // Act
        string json = JsonSerializer.Serialize(comparison);

        // Assert — modern keys use CLR enum names; legacy aliases must be absent.
        Assert.Contains("\"Property\":\"Id\"", json);
        Assert.Contains("\"Operator\":\"Equals\"", json);
        Assert.Contains("\"MatchMode\":\"Single\"", json);
        Assert.Contains("\"Value\":\"100\"", json);
        Assert.Contains("\"Values\":[]", json);
        Assert.DoesNotContain("\"Category\"", json);
        Assert.DoesNotContain("\"Evaluator\"", json);
    }
}
