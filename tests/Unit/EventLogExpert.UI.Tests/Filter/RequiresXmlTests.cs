// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Runtime;
using EventLogExpert.UI.Tests.TestUtils;

namespace EventLogExpert.UI.Tests.Filter;

public sealed class RequiresXmlTests
{
    [Fact]
    public void EventFilter_RequiresXml_WhenAllFiltersAreNonXml_ShouldBeFalse()
    {
        // Arrange
        var filter1 = FilterUtils.CreateTestFilter();
        var filter2 = FilterUtils.CreateTestFilter("Source == \"app\"");
        var eventFilter = new EventFilter(null, [filter1, filter2]);

        // Act + Assert
        Assert.False(eventFilter.RequiresXml);
    }

    [Fact]
    public void EventFilter_RequiresXml_WhenAnyFilterRequiresXml_ShouldBeTrue()
    {
        // Arrange
        var nonXml = FilterUtils.CreateTestFilter();
        var xml = FilterUtils.CreateTestFilter("Xml.Contains(\"x\")");
        var eventFilter = new EventFilter(null, [nonXml, xml]);

        // Act + Assert
        Assert.True(eventFilter.RequiresXml);
    }

    [Fact]
    public void EventFilter_RequiresXml_WhenFiltersListIsEmpty_ShouldBeFalse()
    {
        // Arrange
        var eventFilter = new EventFilter(null, []);

        // Act + Assert
        Assert.False(eventFilter.RequiresXml);
    }
}
