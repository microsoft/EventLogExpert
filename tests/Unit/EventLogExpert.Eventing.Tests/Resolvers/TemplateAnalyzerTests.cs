// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Resolvers;

namespace EventLogExpert.Eventing.Tests.Resolvers;

public sealed class TemplateAnalyzerTests
{
    [Fact]
    public void Analyze_ExtractsMapAttribute_InDocumentOrder()
    {
        var analyzer = new TemplateAnalyzer();

        TemplateMetadata metadata = analyzer.Analyze(
            "<template><data name=\"BusType\" map=\"BusTypeMap\"/><data name=\"Volume\"/></template>");

        Assert.Equal(["BusTypeMap", ""], metadata.AllMaps);
        Assert.Equal(["BusTypeMap", ""], metadata.VisibleMaps);
    }

    [Fact]
    public void Analyze_LengthProviderNode_ExcludedFromVisibleMaps()
    {
        var analyzer = new TemplateAnalyzer();

        TemplateMetadata metadata = analyzer.Analyze(
            "<template>" +
            "<data name=\"Len\" inType=\"win:UInt32\"/>" +
            "<data name=\"Payload\" length=\"Len\" map=\"PayloadMap\"/>" +
            "</template>");

        Assert.Equal(["", "PayloadMap"], metadata.AllMaps);
        Assert.Equal(["PayloadMap"], metadata.VisibleMaps);
    }

    [Fact]
    public void Analyze_NoMapAttribute_YieldsEmptyMapStrings()
    {
        var analyzer = new TemplateAnalyzer();

        TemplateMetadata metadata = analyzer.Analyze(
            "<template><data name=\"Volume\" inType=\"win:UnicodeString\"/></template>");

        Assert.Equal([""], metadata.AllMaps);
    }
}
