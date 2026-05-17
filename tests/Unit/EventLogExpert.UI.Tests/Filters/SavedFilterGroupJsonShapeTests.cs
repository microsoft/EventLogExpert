// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Filtering.Runtime;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using System.Text.Json;

namespace EventLogExpert.UI.Tests.Filters;

public sealed class SavedFilterGroupJsonShapeTests
{
    [Fact]
    public void JsonRoundTrip_EmptyFilters_ProducesEmptyList()
    {
        // Arrange — build via the serializer so the test isn't sensitive to JSON-escaping of the embedded
        // backslash separator in the group Name.
        var original = new SavedFilterGroup { Name = Constants.FilterGroupName, Filters = [] };
        string json = JsonSerializer.Serialize(original);

        // Act
        var restored = JsonSerializer.Deserialize<SavedFilterGroup>(json);

        // Assert
        Assert.NotNull(restored);
        Assert.Equal(Constants.FilterGroupName, restored.Name);
        Assert.Empty(restored.Filters);
    }

    [Fact]
    public void JsonRoundTrip_FiltersArrayDispatchesThroughSavedFilterJsonConverter()
    {
        // Arrange — STJ must dispatch each Filters[i] through the [JsonConverter] on SavedFilter, so a Basic-mode
        // saved filter inside the group keeps its persisted Mode + BasicFilter structure after round-trip. Without
        // this dispatch, F16d's type move could silently break the nested wire shape.
        var group = new SavedFilterGroup
        {
            Name = Constants.FilterGroupName,
            Filters =
            [
                SavedFilter.TryCreate(Constants.FilterIdEquals100, mode: FilterMode.Basic)!
            ]
        };

        // Act
        string json = JsonSerializer.Serialize(group);
        var restored = JsonSerializer.Deserialize<SavedFilterGroup>(json);

        // Assert — wrapping shape uses Name + Filters; inner item carries the SavedFilter converter's Mode field.
        Assert.Contains("\"Name\"", json);
        Assert.Contains("\"Filters\"", json);
        Assert.Contains("\"Mode\":\"Basic\"", json);

        Assert.NotNull(restored);
        Assert.Single(restored.Filters);
        Assert.Equal(FilterMode.Basic, restored.Filters[0].Mode);
        Assert.Equal(Constants.FilterIdEquals100, restored.Filters[0].ComparisonText);
    }

    [Fact]
    public void Write_DefaultShape_OmitsJsonIgnoredProperties()
    {
        // Arrange
        var group = new SavedFilterGroup
        {
            Name = Constants.FilterGroupName,
            Filters = [],
            IsEditing = true
        };

        // Act
        string json = JsonSerializer.Serialize(group);

        // Assert — Id, DisplayName, IsEditing are [JsonIgnore] / derived; only Name + Filters land on disk.
        Assert.Contains("\"Name\"", json);
        Assert.Contains("\"Filters\"", json);
        Assert.DoesNotContain("\"Id\"", json);
        Assert.DoesNotContain("\"DisplayName\"", json);
        Assert.DoesNotContain("\"IsEditing\"", json);
    }

    [Fact]
    public void Write_PreservesName_AndDisplayNameDerivesFromLastSegment()
    {
        // Arrange — DisplayName is derived (last \\-separated segment of Name) and JsonIgnore'd. Reload must
        // reconstitute the same DisplayName from the persisted Name.
        var group = new SavedFilterGroup { Name = Constants.FilterGroupName };

        // Act
        string json = JsonSerializer.Serialize(group);
        var restored = JsonSerializer.Deserialize<SavedFilterGroup>(json);

        // Assert
        Assert.NotNull(restored);
        Assert.Equal(Constants.FilterGroupName, restored.Name);
        Assert.Equal(Constants.FilterGroupDisplayName, restored.DisplayName);
    }
}
