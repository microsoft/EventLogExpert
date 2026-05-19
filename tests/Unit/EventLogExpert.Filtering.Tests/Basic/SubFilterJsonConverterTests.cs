// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;
using System.Text.Json;

namespace EventLogExpert.Filtering.Tests.Basic;

public sealed class SubFilterJsonConverterTests
{
    [Fact]
    public void JsonRoundTrip_AsPartOfBasicFilter_DispatchesThroughConverterForLegacySubFilters()
    {
        // BasicFilter has no custom converter; STJ must still dispatch each ImmutableList<SubFilter> element
        // through SubFilter's [JsonConverter] so legacy "Data" subfilters survive a BasicFilter-level read.
        const string LegacyBasicJson =
            """
            {
              "Comparison": { "Property": "Id", "Operator": "Equals", "MatchMode": "Single", "Value": "100", "Values": [] },
              "SubFilters": [
                { "Data": { "Property": "Level", "Operator": "Equals", "MatchMode": "Single", "Value": "Error", "Values": [] }, "JoinWithAny": false }
              ]
            }
            """;

        var restored = JsonSerializer.Deserialize<BasicFilter>(LegacyBasicJson);

        Assert.NotNull(restored);
        Assert.Equal(EventProperty.Id, restored.Comparison.Property);
        Assert.Single(restored.SubFilters);
        Assert.Equal(EventProperty.Level, restored.SubFilters[0].Comparison.Property);
        Assert.Equal("Error", restored.SubFilters[0].Comparison.Value);
        Assert.False(restored.SubFilters[0].JoinWithAny);

        var fresh = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            },
            ImmutableList.Create(new SubFilter(
                new FilterComparison
                {
                    Property = EventProperty.Level,
                    Operator = ComparisonOperator.Equals,
                    MatchMode = MatchMode.Single,
                    Value = "Error"
                },
                false)));

        string freshJson = JsonSerializer.Serialize(fresh);

        Assert.DoesNotContain("\"Data\"", freshJson);
        Assert.Contains("\"SubFilters\"", freshJson);
        Assert.Contains("\"Comparison\"", freshJson);
    }

    [Fact]
    public void JsonRoundTrip_BothKeysPresent_ModernWinsRegardlessOfOrder()
    {
        const string ModernFirst =
            """
            {
              "Comparison": { "Property": "Level", "Operator": "Equals", "MatchMode": "Single", "Value": "Error", "Values": [] },
              "Data": { "Property": "Id", "Operator": "Equals", "MatchMode": "Single", "Value": "999", "Values": [] },
              "JoinWithAny": false
            }
            """;

        const string LegacyFirst =
            """
            {
              "Data": { "Property": "Id", "Operator": "Equals", "MatchMode": "Single", "Value": "999", "Values": [] },
              "Comparison": { "Property": "Level", "Operator": "Equals", "MatchMode": "Single", "Value": "Error", "Values": [] },
              "JoinWithAny": false
            }
            """;

        var restoredModernFirst = JsonSerializer.Deserialize<SubFilter>(ModernFirst);
        var restoredLegacyFirst = JsonSerializer.Deserialize<SubFilter>(LegacyFirst);

        Assert.NotNull(restoredModernFirst);
        Assert.NotNull(restoredLegacyFirst);

        Assert.Equal(EventProperty.Level, restoredModernFirst.Comparison.Property);
        Assert.Equal("Error", restoredModernFirst.Comparison.Value);

        Assert.Equal(EventProperty.Level, restoredLegacyFirst.Comparison.Property);
        Assert.Equal("Error", restoredLegacyFirst.Comparison.Value);
    }

    [Fact]
    public void JsonRoundTrip_LegacyDataKey_ReadsAsComparison()
    {
        // Legacy persistence used the positional record param name "Data" as the JSON key for what is now Comparison.
        const string LegacyJson =
            """
            { "Data": { "Property": "Id", "Operator": "Equals", "MatchMode": "Single", "Value": "100", "Values": [] }, "JoinWithAny": false }
            """;

        var restored = JsonSerializer.Deserialize<SubFilter>(LegacyJson);

        Assert.NotNull(restored);
        Assert.False(restored.JoinWithAny);
        Assert.Equal(EventProperty.Id, restored.Comparison.Property);
        Assert.Equal("100", restored.Comparison.Value);
    }

    [Fact]
    public void JsonRoundTrip_LegacyDataKeyWithLegacyConditionShape_ReadsBothLayers()
    {
        // Both wrapper (Data) and inner (Category/Evaluator) are legacy; the composed converter dispatch
        // must hydrate both layers in a single deserialize pass.
        const string LegacyJson =
            """
            { "Data": { "Category": 0, "Evaluator": 0, "Value": "200", "Values": [] }, "JoinWithAny": true }
            """;

        var restored = JsonSerializer.Deserialize<SubFilter>(LegacyJson);

        Assert.NotNull(restored);
        Assert.True(restored.JoinWithAny);
        Assert.Equal(EventProperty.Id, restored.Comparison.Property);
        Assert.Equal(ComparisonOperator.Equals, restored.Comparison.Operator);
        Assert.Equal("200", restored.Comparison.Value);
    }

    [Fact]
    public void JsonRoundTrip_ModernShape_PersistsAsComparison_AndRestores()
    {
        var original = new SubFilter(
            new FilterComparison
            {
                Property = EventProperty.Level,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "Error"
            },
            true);

        string json = JsonSerializer.Serialize(original);

        Assert.Contains("\"Comparison\"", json);
        Assert.DoesNotContain("\"Data\"", json);
        Assert.Contains("\"JoinWithAny\":true", json);

        var restored = JsonSerializer.Deserialize<SubFilter>(json);
        Assert.NotNull(restored);
        Assert.True(restored.JoinWithAny);
        Assert.Equal(EventProperty.Level, restored.Comparison.Property);
        Assert.Equal("Error", restored.Comparison.Value);
        Assert.Equal(ComparisonOperator.Equals, restored.Comparison.Operator);
    }
}
