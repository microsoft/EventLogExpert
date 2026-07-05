// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Resolvers;

namespace EventLogExpert.Eventing.Tests.Resolvers;

public sealed class FieldNameOrderingSelectorTests
{
    [Fact]
    public void TrySelectOrdering_CountMatchesNeither_FailsClosed()
    {
        TemplateMetadata meta = Meta("<template><data name=\"A\"/><data name=\"B\"/></template>");

        Assert.False(FieldNameOrderingSelector.TrySelectOrdering(meta, 3, out _));
        Assert.False(FieldNameOrderingSelector.TrySelectOrdering(meta, 1, out _));
    }

    [Fact]
    public void TrySelectOrdering_DefaultMetadata_ReturnsFalseWithoutThrowing()
    {
        Assert.False(FieldNameOrderingSelector.TrySelectOrdering(default, 0, out _));
        Assert.False(FieldNameOrderingSelector.TrySelectOrdering(default, 3, out _));
    }

    [Fact]
    public void TrySelectOrdering_LengthProvider_MatchesVisibleThenAll()
    {
        TemplateMetadata meta = Meta(
            "<template><data name=\"Len\" inType=\"win:UInt32\"/><data name=\"Payload\" length=\"Len\"/></template>");

        Assert.True(FieldNameOrderingSelector.TrySelectOrdering(meta, 1, out FieldNameOrdering visible));
        Assert.Equal(FieldNameOrdering.Visible, visible);

        Assert.True(FieldNameOrderingSelector.TrySelectOrdering(meta, 2, out FieldNameOrdering all));
        Assert.Equal(FieldNameOrdering.All, all);
    }

    [Fact]
    public void TrySelectOrdering_NoLengthProvider_PrefersVisible()
    {
        TemplateMetadata meta = Meta("<template><data name=\"A\"/><data name=\"B\"/></template>");

        Assert.True(FieldNameOrderingSelector.TrySelectOrdering(meta, 2, out FieldNameOrdering ordering));
        Assert.Equal(FieldNameOrdering.Visible, ordering);
    }

    private static TemplateMetadata Meta(string template) => new TemplateAnalyzer().Analyze(template);
}
