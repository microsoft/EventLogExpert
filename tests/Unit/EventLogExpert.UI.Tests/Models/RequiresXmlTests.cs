// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Tests.TestUtils;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.Models;

/// <summary>
/// Validates <see cref="EventFilter.RequiresXml"/> aggregation across filters.
/// This flag drives selective log reloads in <c>EventLogEffects.HandleSetFilters</c>;
/// false positives cause unnecessary reloads, false negatives cause silently broken filters.
/// Per-expression XML detection is covered by <c>FilterCompilerTests</c>.
/// </summary>
public sealed class RequiresXmlTests
{
    [Fact]
    public void EventFilter_RequiresXml_WhenAllFiltersAreNonXml_ShouldBeFalse()
    {
        // Arrange
        var filter1 = FilterUtils.CreateTestFilter("Id == 100");
        var filter2 = FilterUtils.CreateTestFilter("Source == \"app\"");
        var eventFilter = new EventFilter(null, [filter1, filter2]);

        // Act + Assert
        Assert.False(eventFilter.RequiresXml);
    }

    [Fact]
    public void EventFilter_RequiresXml_WhenAnyFilterRequiresXml_ShouldBeTrue()
    {
        // Arrange
        var nonXml = FilterUtils.CreateTestFilter("Id == 100");
        var xml = FilterUtils.CreateTestFilter("Xml.Contains(\"x\")");
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
}
