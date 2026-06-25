// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata;

namespace EventLogExpert.Eventing.Tests.PublisherMetadata;

public sealed class ProviderDetailsAssemblerTests
{
    [Fact]
    public void InjectMapAttribute_DataSourcePrefix_InjectsIntoTheRealDataElement()
    {
        string template = "<template><dataSource name=\"BusType\"/><data name=\"BusType\"/></template>";

        string result = ProviderDetailsAssembler.InjectMapAttribute(template, "BusType", "BusTypeMap");

        Assert.Equal(
            "<template><dataSource name=\"BusType\"/><data name=\"BusType\" map=\"BusTypeMap\"/></template>",
            result);
    }

    [Fact]
    public void InjectMapAttribute_DataSourceWithSameName_IsNotMatched()
    {
        string template = "<template><dataSource name=\"BusType\"/><data name=\"Volume\"/></template>";

        string result = ProviderDetailsAssembler.InjectMapAttribute(template, "BusType", "BusTypeMap");

        Assert.Equal(template, result);
        Assert.DoesNotContain("map=", result);
    }

    [Fact]
    public void InjectMapAttribute_FieldNotPresent_ReturnsTemplateUnchanged()
    {
        string template = "<template><data name=\"Other\" inType=\"win:UInt32\"/></template>";

        string result = ProviderDetailsAssembler.InjectMapAttribute(template, "BusType", "BusTypeMap");

        Assert.Equal(template, result);
    }

    [Fact]
    public void InjectMapAttribute_InsertsMapAfterMatchingDataField()
    {
        string template = "<template><data name=\"BusType\" inType=\"win:UInt32\"/></template>";

        string result = ProviderDetailsAssembler.InjectMapAttribute(template, "BusType", "BusTypeMap");

        Assert.Equal(
            "<template><data name=\"BusType\" map=\"BusTypeMap\" inType=\"win:UInt32\"/></template>",
            result);
    }

    [Fact]
    public void InjectMapAttribute_PrefixFieldName_DoesNotMisfire()
    {
        string template = "<template><data name=\"BusType\"/></template>";

        string result = ProviderDetailsAssembler.InjectMapAttribute(template, "Bus", "BusMap");

        Assert.Equal(template, result);
    }

    [Fact]
    public void InjectMapAttribute_SecondDataField_InjectsIntoTheNamedElement()
    {
        string template = "<template><data name=\"Bus\"/><data name=\"BusType\"/></template>";

        string result = ProviderDetailsAssembler.InjectMapAttribute(template, "BusType", "BusTypeMap");

        Assert.Equal(
            "<template><data name=\"Bus\"/><data name=\"BusType\" map=\"BusTypeMap\"/></template>",
            result);
    }

    [Fact]
    public void InjectMapAttribute_StructWithSameName_IsNotMatched()
    {
        string template = "<template><struct name=\"BusType\"/></template>";

        string result = ProviderDetailsAssembler.InjectMapAttribute(template, "BusType", "BusTypeMap");

        Assert.Equal(template, result);
    }
}
