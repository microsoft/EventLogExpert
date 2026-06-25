// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata;

namespace EventLogExpert.Eventing.Tests.PublisherMetadata;

public sealed class ProviderDetailsAssemblerTests
{
    [Fact]
    public void Assemble_DuplicateProjectedOpcodeKey_KeepsFirstAndPreservesDistinctKeys()
    {
        // Opcode keys are the value shifted right 16 bits: 0x00010000 and 0x0001FFFF both project to 1; 0x00020000 to 2.
        // First write wins for the colliding key, and the distinct key survives (the dedup the native getters used to do).
        var content = CreateContent(
            resolveMessage: _ => null,
            opcodes:
            [
                new RawNamedValue(0x00010000, uint.MaxValue, "First"),
                new RawNamedValue(0x0001FFFF, uint.MaxValue, "Second"),
                new RawNamedValue(0x00020000, uint.MaxValue, "Third")
            ]);

        var details = ProviderDetailsAssembler.Assemble(content, null);

        Assert.Equal(2, details.Opcodes.Count);
        Assert.Equal("First", details.Opcodes[1]);
        Assert.Equal("Third", details.Opcodes[2]);
    }

    [Fact]
    public void Assemble_Event_ExpandsKeywordMaskAndResolvesDescriptionAndLogName()
    {
        var content = CreateContent(
            resolveMessage: id => id == 50 ? "Event description" : null,
            channels: new Dictionary<uint, string> { [16] = "Operational" },
            events:
            [
                new RawProviderEvent(
                    Id: 4624,
                    Version: 1,
                    ChannelId: 16,
                    Level: 0,
                    Opcode: 0,
                    Task: 0,
                    KeywordsMask: 0x8000000000000001,
                    Template: "<template/>",
                    MessageId: 50)
            ]);

        var details = ProviderDetailsAssembler.Assemble(content, null);

        var model = Assert.Single(details.Events);
        Assert.Equal(4624L, model.Id);
        Assert.Equal((byte)1, model.Version);
        Assert.Equal("Event description", model.Description);
        Assert.Equal("Operational", model.LogName);
        // Keyword mask expands MSB-first: bit 63 then bit 0.
        Assert.Equal([unchecked((long)0x8000000000000000), 1L], model.Keywords);
    }

    [Fact]
    public void Assemble_EventWithoutMessageId_DescriptionIsEmptyString()
    {
        var content = CreateContent(
            resolveMessage: _ => "ShouldNotBeUsed",
            events: [new RawProviderEvent(1, 0, 0, 0, 0, 0, 0, "<t/>", uint.MaxValue)]);

        var details = ProviderDetailsAssembler.Assemble(content, null);

        var model = Assert.Single(details.Events);
        Assert.Equal(string.Empty, model.Description);
    }

    [Fact]
    public void Assemble_MessageIdResolvesToNull_NameIsEmptyString()
    {
        // The offline resolver may return null; the assembler coalesces that to string.Empty (never null), so the
        // encoder hashes it the same as the native path.
        var content = CreateContent(
            resolveMessage: _ => null,
            keywords: [new RawNamedValue(0x0000000000000002, 99, null)]);

        var details = ProviderDetailsAssembler.Assemble(content, null);

        Assert.Equal(string.Empty, details.Keywords[2L]);
    }

    [Fact]
    public void Assemble_NamedValueWithMessageId_ResolvesNameThroughResolver()
    {
        var content = CreateContent(
            resolveMessage: id => id == 42 ? "ResolvedKeyword" : null,
            keywords: [new RawNamedValue(0x0000000000000001, 42, "ShouldNotBeUsed")]);

        var details = ProviderDetailsAssembler.Assemble(content, null);

        // The message id wins over the inline name when a real id exists.
        Assert.Equal("ResolvedKeyword", details.Keywords[1L]);
    }

    [Fact]
    public void Assemble_NamedValueWithoutMessageId_FallsBackToInlineName()
    {
        var content = CreateContent(
            resolveMessage: _ => "ShouldNotBeUsed",
            opcodes: [new RawNamedValue(0x00010000, uint.MaxValue, "InlineOpcode")]);

        var details = ProviderDetailsAssembler.Assemble(content, null);

        Assert.Equal("InlineOpcode", details.Opcodes[1]);
    }

    [Fact]
    public void Assemble_TaskValue_UsesValueAsKey()
    {
        var content = CreateContent(
            resolveMessage: _ => null,
            tasks: [new RawNamedValue(7, uint.MaxValue, "InlineTask")]);

        var details = ProviderDetailsAssembler.Assemble(content, null);

        Assert.Equal("InlineTask", details.Tasks[7]);
    }

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

    private static RawProviderContent CreateContent(
        Func<uint, string?> resolveMessage,
        IReadOnlyDictionary<uint, string>? channels = null,
        IReadOnlyList<RawProviderEvent>? events = null,
        IReadOnlyList<RawNamedValue>? keywords = null,
        IReadOnlyList<RawNamedValue>? opcodes = null,
        IReadOnlyList<RawNamedValue>? tasks = null) =>
        new()
        {
            // Guid.Empty short-circuits value-map population, so the assembler never touches the native WEVT reader.
            ProviderName = "TestProvider",
            PublisherGuid = Guid.Empty,
            ResourceFilePath = string.Empty,
            ResolveMessage = resolveMessage,
            Channels = channels ?? new Dictionary<uint, string>(),
            Events = events ?? [],
            Keywords = keywords ?? [],
            Opcodes = opcodes ?? [],
            Tasks = tasks ?? []
        };
}
