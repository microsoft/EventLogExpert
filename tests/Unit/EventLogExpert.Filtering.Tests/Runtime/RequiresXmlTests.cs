// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Runtime;
using EventLogExpert.Filtering.TestUtils;

namespace EventLogExpert.Filtering.Tests.Runtime;

public sealed class RequiresXmlTests
{
    [Fact]
    public void Filter_RequiresXml_WhenAllFiltersAreNonXml_ShouldBeFalse()
    {
        // Arrange
        var filter1 = FilterFixtures.CreateTestFilter();
        var filter2 = FilterFixtures.CreateTestFilter("Source == \"app\"");
        var filter = new Filter(null, [filter1, filter2]);

        // Act + Assert
        Assert.False(filter.RequiresXml);
    }

    [Fact]
    public void Filter_RequiresXml_WhenAnyFilterRequiresXml_ShouldBeTrue()
    {
        // Arrange
        var nonXml = FilterFixtures.CreateTestFilter();
        var xml = FilterFixtures.CreateTestFilter("Xml.Contains(\"x\")");
        var filter = new Filter(null, [nonXml, xml]);

        // Act + Assert
        Assert.True(filter.RequiresXml);
    }

    [Fact]
    public void Filter_RequiresXml_WhenFiltersListIsEmpty_ShouldBeFalse()
    {
        // Arrange
        var filter = new Filter(null, []);

        // Act + Assert
        Assert.False(filter.RequiresXml);
    }
}
