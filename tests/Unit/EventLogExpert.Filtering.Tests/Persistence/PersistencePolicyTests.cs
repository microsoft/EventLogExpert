// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Filtering.TestUtils.Constants;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EventLogExpert.Filtering.Tests.Persistence;

public sealed partial class PersistencePolicyTests
{
    [Fact]
    public void BasicFilter_Write_PinsNestedComparisonAndSubFiltersKeys()
    {
        // Arrange
        var basicFilter = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            },
            []);

        // Act
        string json = JsonSerializer.Serialize(basicFilter);

        // Assert
        Assert.Contains("\"Comparison\"", json);
        Assert.Contains("\"SubFilters\"", json);
        Assert.DoesNotContain("\"Condition\"", json);
    }

    [Fact]
    public void FilterMode_MemberNames_AreFrozenAcrossF16()
    {
        // Act + Assert
        Assert.Equal("Basic", nameof(FilterMode.Basic));
        Assert.Equal("Advanced", nameof(FilterMode.Advanced));
        Assert.Equal("Cached", nameof(FilterMode.Cached));

        var declared = Enum.GetNames<FilterMode>().ToHashSet();
        Assert.Equal(["Basic", "Advanced", "Cached"], declared);
    }

    [Fact]
    public void HighlightColor_MemberCount_IsFrozenAcrossF16()
    {
        // Act + Assert
        Assert.Equal(28, Enum.GetValues<HighlightColor>().Length);
    }

    [Theory]
    [InlineData(HighlightColor.None, 0)]
    [InlineData(HighlightColor.LightRed, 1)]
    [InlineData(HighlightColor.Red, 2)]
    [InlineData(HighlightColor.DarkRed, 3)]
    [InlineData(HighlightColor.LightOrange, 4)]
    [InlineData(HighlightColor.Orange, 5)]
    [InlineData(HighlightColor.DarkOrange, 6)]
    [InlineData(HighlightColor.LightYellow, 7)]
    [InlineData(HighlightColor.Yellow, 8)]
    [InlineData(HighlightColor.DarkYellow, 9)]
    [InlineData(HighlightColor.LightGreen, 10)]
    [InlineData(HighlightColor.Green, 11)]
    [InlineData(HighlightColor.DarkGreen, 12)]
    [InlineData(HighlightColor.LightTeal, 13)]
    [InlineData(HighlightColor.Teal, 14)]
    [InlineData(HighlightColor.DarkTeal, 15)]
    [InlineData(HighlightColor.LightBlue, 16)]
    [InlineData(HighlightColor.Blue, 17)]
    [InlineData(HighlightColor.DarkBlue, 18)]
    [InlineData(HighlightColor.LightPurple, 19)]
    [InlineData(HighlightColor.Purple, 20)]
    [InlineData(HighlightColor.DarkPurple, 21)]
    [InlineData(HighlightColor.LightMagenta, 22)]
    [InlineData(HighlightColor.Magenta, 23)]
    [InlineData(HighlightColor.DarkMagenta, 24)]
    [InlineData(HighlightColor.LightPink, 25)]
    [InlineData(HighlightColor.Pink, 26)]
    [InlineData(HighlightColor.DarkPink, 27)]
    public void HighlightColor_OrdinalValue_IsFrozenAcrossF16(HighlightColor member, int expectedOrdinal)
    {
        // Act + Assert
        Assert.Equal(expectedOrdinal, (int)member);
    }

    [Fact]
    public void SavedFilter_BasicMode_Json_ContainsLiteralBasicFilterKey()
    {
        // Arrange
        var filter = SavedFilter.TryCreate(FilterTestConstants.FilterIdEquals100, mode: FilterMode.Basic);
        Assert.NotNull(filter);
        Assert.NotNull(filter.BasicFilter);

        // Act
        string json = JsonSerializer.Serialize(filter);

        // Assert
        Assert.Contains("\"BasicFilter\"", json);
        Assert.DoesNotContain("\"StructuredFilter\"", json);
    }

    [Fact]
    public void SavedFilter_BasicMode_Read_AcceptsLiteralBasicFilterKey()
    {
        // Arrange
        const string PersistedJson =
            $$"""
              {
                "Color": 0,
                "ComparisonText": "{{FilterTestConstants.FilterIdEquals100}}",
                "IsExcluded": false,
                "Mode": "Basic",
                "BasicFilter": {
                  "Comparison": { "Property": "Id", "Operator": "Equals", "MatchMode": "Single", "Value": "100", "Values": [] },
                  "SubFilters": []
                }
              }
              """;

        // Act
        var restored = JsonSerializer.Deserialize<SavedFilter>(PersistedJson);

        // Assert
        Assert.NotNull(restored);
        Assert.Equal(FilterMode.Basic, restored.Mode);
        Assert.NotNull(restored.BasicFilter);
        Assert.Equal(EventProperty.Id, restored.BasicFilter.Comparison.Property);
        Assert.Equal("100", restored.BasicFilter.Comparison.Value);
    }

    [Theory]
    [InlineData(FilterMode.Basic, "\"Mode\":\"Basic\"")]
    [InlineData(FilterMode.Advanced, "\"Mode\":\"Advanced\"")]
    [InlineData(FilterMode.Cached, "\"Mode\":\"Cached\"")]
    public void SavedFilter_ModeJson_IsStringValuedForEveryMode(FilterMode mode, string expectedFragment)
    {
        // Arrange
        var filter = SavedFilter.TryCreate(FilterTestConstants.FilterIdEquals100, mode: mode);
        Assert.NotNull(filter);

        // Act
        string json = JsonSerializer.Serialize(filter);

        // Assert
        Assert.Contains(expectedFragment, json);
    }

    [Theory]
    [InlineData(0, FilterMode.Advanced)]
    [InlineData(1, FilterMode.Basic)]
    [InlineData(2, FilterMode.Cached)]
    public void SavedFilter_NumericMode_IsAccepted_ByConverter(int numericMode, FilterMode expectedMode)
    {
        // Arrange
        string json =
            $$"""
              {
                "Color": 0,
                "ComparisonText": "{{FilterTestConstants.FilterIdEquals100}}",
                "IsExcluded": false,
                "Mode": {{numericMode}}
              }
              """;

        // Act
        var restored = JsonSerializer.Deserialize<SavedFilter>(json);

        // Assert
        Assert.NotNull(restored);
        Assert.Equal(expectedMode, restored.Mode);
    }

    [Fact]
    public void SavedFilter_StringHighlightColor_IsAccepted_ByConverter()
    {
        // Arrange
        const string Json =
            $$"""
              {
                "Color": "LightRed",
                "ComparisonText": "{{FilterTestConstants.FilterIdEquals100}}",
                "IsExcluded": false,
                "Mode": "Advanced"
              }
              """;

        // Act
        var restored = JsonSerializer.Deserialize<SavedFilter>(Json);

        // Assert
        Assert.NotNull(restored);
        Assert.Equal(HighlightColor.LightRed, restored.Color);
    }

    [Theory]
    [InlineData(FilterMode.Advanced)]
    [InlineData(FilterMode.Cached)]
    public void SavedFilter_Write_NonBasicMode_OmitsBasicFilterKey(FilterMode mode)
    {
        // Arrange
        var filter = SavedFilter.TryCreate(FilterTestConstants.FilterIdEquals100, mode: mode);
        Assert.NotNull(filter);
        Assert.Null(filter.BasicFilter);

        // Act
        string json = JsonSerializer.Serialize(filter);

        // Assert
        Assert.DoesNotContain("\"BasicFilter\"", json);
    }

    [Fact]
    public void SavedFilter_Write_OmitsJsonIgnoredProperties()
    {
        // Arrange
        var filter = SavedFilter.TryCreate(FilterTestConstants.FilterIdEquals100, mode: FilterMode.Basic);
        Assert.NotNull(filter);

        // Act
        string json = JsonSerializer.Serialize(filter);

        // Assert
        Assert.DoesNotContain("\"Id\":", json);
        Assert.DoesNotContain("\"Compiled\":", json);
        Assert.DoesNotContain("\"IsEnabled\":", json);
    }

    [Fact]
    public void SavedFilter_Write_PinsNumericHighlightColor()
    {
        // Arrange
        var filter = SavedFilter.TryCreate(
            FilterTestConstants.FilterIdEquals100,
            color: HighlightColor.Blue,
            mode: FilterMode.Advanced);

        Assert.NotNull(filter);

        // Act
        string json = JsonSerializer.Serialize(filter);

        // Assert
        Assert.Contains("\"Color\":17", json);
        Assert.DoesNotContain("\"Color\":\"Blue\"", json);
    }

    [Fact]
    public void SavedFiltersStorageKey_LiteralValue_IsFrozenAcrossF16()
    {
        // Arrange
        string preferencesProviderPath = ResolveRepoRelativePath(
            "src",
            "EventLogExpert",
            "Adapters",
            "Settings",
            "FilterGroupPreferencesAdapter.cs");

        string source = File.ReadAllText(preferencesProviderPath);

        // Act + Assert
        Assert.Matches(
            MyRegex(),
            source);

        Assert.Contains("Preferences.Default.Get(SavedFilters, \"[]\")", source);
        Assert.Contains("Preferences.Default.Set(SavedFilters, JsonSerializer.Serialize(value))", source);
    }

    [GeneratedRegex("""private\s+const\s+string\s+SavedFilters\s*=\s*"saved-filters"\s*;""")]
    private static partial Regex MyRegex();

    private static string ResolveRepoRelativePath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EventLogExpert.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        string combined = Path.Combine([directory.FullName, .. segments]);
        Assert.True(File.Exists(combined), $"Expected source file at {combined} to exist.");
        return combined;
    }
}
