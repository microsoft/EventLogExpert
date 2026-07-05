// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Resolvers;

namespace EventLogExpert.Eventing.Tests.Resolvers;

public sealed class TemplateFieldSchemaTests
{
    [Fact]
    public void Schema_LengthProviderNode_KeepsRealNameInAll_ExcludedFromVisible()
    {
        TemplateInfo info = Info(
            "<template><data name=\"Len\" inType=\"win:UInt32\"/><data name=\"Payload\" length=\"Len\"/></template>");

        Assert.Equal(["Len", "Payload"], info.Schema.AllNames);
        Assert.Equal(["Payload"], info.Schema.VisibleNames);
    }

    [Fact]
    public void Schema_NameLengths_MatchOutTypeLengths_Lockstep()
    {
        TemplateInfo info = Info(
            "<template>" +
            "<data name=\"Len\" inType=\"win:UInt32\"/>" +
            "<data name=\"Payload\" length=\"Len\"/>" +
            "<data name=\"Extra\"/>" +
            "</template>");

        Assert.Equal(info.Metadata.AllOutTypes.Length, info.Schema.AllNames.Length);
        Assert.Equal(info.Metadata.VisibleOutTypes.Length, info.Schema.VisibleNames.Length);
    }

    [Fact]
    public void TryGetIndex_ConcurrentFirstAccess_BuildsOneCompleteMap()
    {
        TemplateInfo info = Info("<template><data name=\"A\"/><data name=\"B\"/><data name=\"C\"/></template>");

        Parallel.For(0, 128, _ =>
        {
            Assert.True(info.Schema.TryGetIndex(FieldNameOrdering.All, "A", out int a));
            Assert.Equal(0, a);
            Assert.True(info.Schema.TryGetIndex(FieldNameOrdering.All, "C", out int c));
            Assert.Equal(2, c);
        });
    }

    [Fact]
    public void TryGetIndex_DuplicateName_ReturnsFirstIndex()
    {
        TemplateInfo info = Info("<template><data name=\"Dup\"/><data name=\"Dup\"/></template>");

        Assert.True(info.Schema.TryGetIndex(FieldNameOrdering.All, "Dup", out int index));
        Assert.Equal(0, index);
    }

    [Fact]
    public void TryGetIndex_EmptyName_IsNotIndexable()
    {
        var schema = new TemplateFieldSchema(["", "Named"], ["", "Named"]);

        Assert.False(schema.TryGetIndex(FieldNameOrdering.All, "", out _));
        Assert.True(schema.TryGetIndex(FieldNameOrdering.All, "Named", out int index));
        Assert.Equal(1, index);
    }

    [Fact]
    public void TryGetIndex_ResolvesNameInBothOrderings()
    {
        TemplateInfo info = Info("<template><data name=\"A\"/><data name=\"B\"/></template>");

        Assert.True(info.Schema.TryGetIndex(FieldNameOrdering.All, "B", out int allIndex));
        Assert.Equal(1, allIndex);

        Assert.True(info.Schema.TryGetIndex(FieldNameOrdering.Visible, "A", out int visibleIndex));
        Assert.Equal(0, visibleIndex);

        Assert.False(info.Schema.TryGetIndex(FieldNameOrdering.All, "Missing", out _));
    }

    private static TemplateInfo Info(string template) => new TemplateAnalyzer().GetTemplateInfo(template);
}
