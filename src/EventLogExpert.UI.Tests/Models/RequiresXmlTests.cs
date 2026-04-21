// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.Models;

/// <summary>
/// Validates <see cref="FilterComparison.RequiresXml"/> detection and
/// <see cref="EventFilter.RequiresXml"/> aggregation across (sub)filters.
/// These flags drive selective log reloads in <c>EventLogEffects.HandleSetFilters</c>;
/// false positives cause unnecessary reloads, false negatives cause silently broken filters.
/// </summary>
public sealed class RequiresXmlTests
{
    [Fact]
    public void EventFilter_RequiresXml_WhenAllFiltersAreNonXml_ShouldBeFalse()
    {
        // Arrange
        var filter1 = new FilterModel { Comparison = new FilterComparison { Value = "Id == 100" } };
        var filter2 = new FilterModel { Comparison = new FilterComparison { Value = "Source == \"app\"" } };
        var eventFilter = new EventFilter(null, [filter1, filter2]);

        // Act + Assert
        Assert.False(eventFilter.RequiresXml);
    }

    [Fact]
    public void EventFilter_RequiresXml_WhenAnyTopLevelFilterRequiresXml_ShouldBeTrue()
    {
        // Arrange
        var nonXml = new FilterModel { Comparison = new FilterComparison { Value = "Id == 100" } };
        var xml = new FilterModel { Comparison = new FilterComparison { Value = "Xml.Contains(\"x\")" } };
        var eventFilter = new EventFilter(null, [nonXml, xml]);

        // Act + Assert
        Assert.True(eventFilter.RequiresXml);
    }

    [Fact]
    public void EventFilter_RequiresXml_WhenFiltersListIsEmpty_ShouldBeFalse()
    {
        // Arrange
        var eventFilter = new EventFilter(null, ImmutableList<FilterModel>.Empty);

        // Act + Assert
        Assert.False(eventFilter.RequiresXml);
    }

    [Fact]
    public void EventFilter_RequiresXml_WhenSubFilterRequiresXml_ShouldBeTrue()
    {
        // Arrange — top-level filter is non-XML but a sub-filter touches Xml.
        var subFilter = new FilterModel { Comparison = new FilterComparison { Value = "Xml.Contains(\"y\")" } };

        var topFilter = new FilterModel
        {
            Comparison = new FilterComparison { Value = "Id == 100" },
            SubFilters = [subFilter]
        };

        var eventFilter = new EventFilter(null, [topFilter]);

        // Act + Assert
        Assert.True(eventFilter.RequiresXml);
    }

    [Theory]
    [InlineData("Xml.Contains(\"foo\")", true)]
    [InlineData("Xml != null && Xml.Contains(\"a\")", true)]
    [InlineData("Id == 100 || Xml.Contains(\"foo\")", true)]
    [InlineData("Id == 100 && Source == \"app\"", false)]
    [InlineData("Description.Contains(\"error\")", false)]
    [InlineData("Level == \"Error\" && (Id == 1 || Id == 2)", false)]
    public void FilterComparison_RequiresXml_ShouldReflectXmlMemberAccess(string expression, bool expected)
    {
        // Arrange + Act
        var comparison = new FilterComparison { Value = expression };

        // Assert
        Assert.Equal(expected, comparison.RequiresXml);
    }
}
