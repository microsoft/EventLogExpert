// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Resolvers;

namespace EventLogExpert.Eventing.Tests.Resolvers;

public sealed class TemplateAnalyzerTests
{
    [Theory]
    [InlineData("M&amp;N", "M&N")]
    [InlineData("M&lt;N&gt;", "M<N>")]
    [InlineData("M&quot;N&apos;P", "M\"N'P")]
    [InlineData("M&amp;lt;N", "M&lt;N")]
    public void Analyze_EscapedMapName_IsXmlUnescapedToRawKey(string escapedMap, string expectedMap)
    {
        var analyzer = new TemplateAnalyzer();

        // Ensures ampersand unescaping runs last so escaped entity bodies are not decoded twice.
        TemplateMetadata metadata = analyzer.Analyze(
            $"<template><data name=\"Field\" map=\"{escapedMap}\"/></template>");

        Assert.Equal([expectedMap], metadata.AllMaps);
    }

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

    [Fact]
    public void EventHasNoPropertiesButTemplateHasSome_CountsVisibleNodesExcludingLengthProviders()
    {
        var analyzer = new TemplateAnalyzer();

        // Len is a length-provider node EvtRender consumes, so the visible count is 1 (Payload only) - still > 0.
        const string template =
            "<template>" +
            "<data name=\"Len\" inType=\"win:UInt32\"/>" +
            "<data name=\"Payload\" length=\"Len\" inType=\"win:Binary\"/>" +
            "</template>";

        Assert.True(analyzer.EventHasNoPropertiesButTemplateHasSome(template, 0));
        Assert.False(analyzer.EventHasNoPropertiesButTemplateHasSome(template, 1));
    }

    [Fact]
    public void EventHasNoPropertiesButTemplateHasSome_EmptyTemplate_IsFalse()
    {
        var analyzer = new TemplateAnalyzer();

        Assert.False(analyzer.EventHasNoPropertiesButTemplateHasSome("<template></template>", 0));
    }

    [Theory]
    [InlineData(0, true)]  // A zero-insert record still matches its exact 3-field definition.
    [InlineData(2, false)] // A partial (some-but-fewer) count stays conservative and is not accepted here.
    [InlineData(3, false)] // The exact count is handled by the strict path, not this one.
    [InlineData(4, false)] // More inserts than the template declares is a genuine shape mismatch.
    public void EventHasNoPropertiesButTemplateHasSome_OnlyTrueForZeroInsertRecords(int eventPropertyCount, bool expected)
    {
        var analyzer = new TemplateAnalyzer();

        const string template =
            "<template>" +
            "<data name=\"A\" inType=\"win:UnicodeString\"/>" +
            "<data name=\"B\" inType=\"win:UnicodeString\"/>" +
            "<data name=\"C\" inType=\"win:UInt32\"/>" +
            "</template>";

        Assert.Equal(expected, analyzer.EventHasNoPropertiesButTemplateHasSome(template, eventPropertyCount));
    }
}
