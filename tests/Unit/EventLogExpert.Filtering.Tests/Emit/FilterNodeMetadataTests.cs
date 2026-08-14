// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Emit;
using EventLogExpert.Filtering.Lowering;

namespace EventLogExpert.Filtering.Tests.Emit;

public sealed class FilterNodeMetadataTests
{
    [Fact]
    public void PartitionAndChain_WhenAllConditionsAreCheap_PutsAllInCheapAndNoneInXml()
    {
        var cheapA = CheapLeaf(ResolvedEventField.Source);
        var cheapB = CheapLeaf(ResolvedEventField.ComputerName);
        AndNode root = new(cheapA, cheapB);

        var (cheapConditions, xmlConditions) = FilterNodeMetadata.PartitionAndChain(root);

        FilterNode[] expectedCheap = [cheapA, cheapB];
        Assert.Equal(expectedCheap, cheapConditions);
        Assert.Empty(xmlConditions);
    }

    [Fact]
    public void PartitionAndChain_WhenAllConditionsReferenceXml_PutsAllInXmlAndNoneInCheap()
    {
        var xmlA = XmlLeaf("a");
        var xmlB = XmlLeaf("b");
        AndNode root = new(xmlA, xmlB);

        var (cheapConditions, xmlConditions) = FilterNodeMetadata.PartitionAndChain(root);

        FilterNode[] expectedXml = [xmlA, xmlB];
        Assert.Empty(cheapConditions);
        Assert.Equal(expectedXml, xmlConditions);
    }

    [Fact]
    public void PartitionAndChain_WhenConditionsAreMixed_SeparatesByXmlReferencePreservingOrder()
    {
        var cheapA = CheapLeaf(ResolvedEventField.Source);
        var xmlB = XmlLeaf();
        var cheapC = CheapLeaf(ResolvedEventField.ComputerName);
        AndNode root = new(new AndNode(cheapA, xmlB), cheapC);

        var (cheapConditions, xmlConditions) = FilterNodeMetadata.PartitionAndChain(root);

        FilterNode[] expectedCheap = [cheapA, cheapC];
        FilterNode[] expectedXml = [xmlB];
        Assert.Equal(expectedCheap, cheapConditions);
        Assert.Equal(expectedXml, xmlConditions);
    }

    [Fact]
    public void PartitionAndChain_WhenNodeIsASingleXmlLeaf_ReturnsItAsTheOnlyXmlCondition()
    {
        var xml = XmlLeaf();

        var (cheapConditions, xmlConditions) = FilterNodeMetadata.PartitionAndChain(xml);

        FilterNode[] expectedXml = [xml];
        Assert.Empty(cheapConditions);
        Assert.Equal(expectedXml, xmlConditions);
    }

    [Fact]
    public void PartitionAndChain_WhenXmlIsNestedUnderNot_KeepsTheWholeNotSubtreeAsOneXmlCondition()
    {
        var cheapA = CheapLeaf(ResolvedEventField.Source);
        var xmlB = XmlLeaf();
        NotNode negatedXml = new(xmlB);
        AndNode root = new(cheapA, negatedXml);

        var (cheapConditions, xmlConditions) = FilterNodeMetadata.PartitionAndChain(root);

        FilterNode[] expectedCheap = [cheapA];
        FilterNode[] expectedXml = [negatedXml];
        Assert.Equal(expectedCheap, cheapConditions);
        Assert.Equal(expectedXml, xmlConditions);
    }

    [Fact]
    public void PartitionAndChain_WhenXmlIsNestedUnderOr_KeepsTheWholeOrSubtreeAsOneXmlCondition()
    {
        var cheapA = CheapLeaf(ResolvedEventField.Source);
        var cheapB = CheapLeaf(ResolvedEventField.ComputerName);
        var xmlC = XmlLeaf();
        OrNode mixedOr = new(cheapB, xmlC);
        AndNode root = new(cheapA, mixedOr);

        var (cheapConditions, xmlConditions) = FilterNodeMetadata.PartitionAndChain(root);

        FilterNode[] expectedCheap = [cheapA];
        FilterNode[] expectedXml = [mixedOr];
        Assert.Equal(expectedCheap, cheapConditions);
        Assert.Equal(expectedXml, xmlConditions);
    }

    private static ContainsNode CheapLeaf(ResolvedEventField field) => new(field, "needle", true);

    private static ContainsNode XmlLeaf(string needle = "needle") => new(ResolvedEventField.Xml, needle, true);
}
