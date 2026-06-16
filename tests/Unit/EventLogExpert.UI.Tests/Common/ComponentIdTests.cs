// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.UI.Common;

namespace EventLogExpert.UI.Tests.Common;

public sealed class ComponentIdTests
{
    private static readonly Guid s_guid = Guid.Parse("0123456789abcdef0123456789abcdef");

    [Fact]
    public void Default_IsEmpty_ValueThrows_ToStringEmpty()
    {
        ComponentId id = default;

        Assert.True(id.IsEmpty);
        Assert.Equal(string.Empty, id.ToString());
        Assert.Throws<InvalidOperationException>(() => id.Value);
    }

    [Fact]
    public void For_EmptyFilterId_Throws()
    {
        Assert.Throws<ArgumentException>(() => ComponentId.For(new FilterId(Guid.Empty), ComponentIdScope.PaneFilter));
    }

    [Fact]
    public void For_EmptyLibraryEntryId_Throws()
    {
        Assert.Throws<ArgumentException>(() => ComponentId.For(new LibraryEntryId(Guid.Empty)));
    }

    [Fact]
    public void For_FilterId_FormatsScopedId()
    {
        var filterId = new FilterId(s_guid);

        Assert.Equal("fp-0123456789abcdef0123456789abcdef", ComponentId.For(filterId, ComponentIdScope.PaneFilter).Value);
        Assert.Equal("fp-pending-0123456789abcdef0123456789abcdef", ComponentId.For(filterId, ComponentIdScope.PanePendingFilter).Value);
        Assert.Equal("lef-0123456789abcdef0123456789abcdef", ComponentId.For(filterId, ComponentIdScope.LibraryFilter).Value);
        Assert.Equal("lef-pending-0123456789abcdef0123456789abcdef", ComponentId.For(filterId, ComponentIdScope.LibraryPendingFilter).Value);
        Assert.Equal("sf-0123456789abcdef0123456789abcdef", ComponentId.For(filterId, ComponentIdScope.Predicate).Value);
    }

    [Fact]
    public void For_LibraryEntryId_FormatsId()
    {
        Assert.Equal("le-0123456789abcdef0123456789abcdef", ComponentId.For(new LibraryEntryId(s_guid)).Value);
    }

    [Fact]
    public void For_SameFilterId_DifferentScopes_ProduceDifferentIds()
    {
        var filterId = new FilterId(s_guid);

        var pane = ComponentId.For(filterId, ComponentIdScope.PaneFilter).Value;
        var library = ComponentId.For(filterId, ComponentIdScope.LibraryFilter).Value;

        Assert.NotEqual(pane, library);
    }

    [Fact]
    public void For_UnknownScope_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ComponentId.For(new FilterId(s_guid), (ComponentIdScope)999));
    }

    [Fact]
    public void NewUnique_ProducesDistinctValues()
    {
        var ids = Enumerable.Range(0, 1000).Select(_ => ComponentId.NewUnique().Value).ToHashSet();

        Assert.Equal(1000, ids.Count);
    }

    [Fact]
    public void NewUnique_StartsWithAlphabeticPrefix_AndIsValid()
    {
        var id = ComponentId.NewUnique().Value;

        Assert.StartsWith("cid-", id);
        Assert.True(char.IsAsciiLetter(id[0]));
        Assert.DoesNotContain(' ', id);
        Assert.Matches(ComponentId.ValidIdPattern, id);
    }

    [Fact]
    public void Suffix_AppendsParts()
    {
        var id = ComponentId.For(new FilterId(s_guid), ComponentIdScope.PaneFilter)
            .Suffix("main")
            .Suffix("Comparison")
            .Value;

        Assert.Equal("fp-0123456789abcdef0123456789abcdef_main_Comparison", id);
    }

    [Fact]
    public void Suffix_OnDefault_Throws()
    {
        ComponentId id = default;

        Assert.Throws<InvalidOperationException>(() => id.Suffix("main"));
    }

    [Fact]
    public void Suffix_WithEmptyPart_Throws()
    {
        var id = ComponentId.NewUnique();

        Assert.Throws<ArgumentException>(() => id.Suffix(""));
    }

    [Fact]
    public void Suffix_WithWhitespace_Throws()
    {
        var id = ComponentId.NewUnique();

        Assert.Throws<ArgumentException>(() => id.Suffix("bad part"));
    }

    [Fact]
    public void ToString_ReturnsValue()
    {
        var id = ComponentId.NewUnique();

        Assert.Equal(id.Value, id.ToString());
    }
}
