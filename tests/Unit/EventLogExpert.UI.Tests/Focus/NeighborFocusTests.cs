// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Focus;

namespace EventLogExpert.UI.Tests.Focus;

public sealed class NeighborFocusTests
{
    private static readonly Func<string, bool> Always = _ => true;

    [Fact]
    public void TryGetNeighborAfterRemove_EmptyList_ReturnsFalse()
    {
        bool found = NeighborFocus.TryGetNeighborAfterRemove(Array.Empty<string>(), removedIndex: 0, Always, out _);

        Assert.False(found);
    }

    [Fact]
    public void TryGetNeighborAfterRemove_ForwardNeighborNotFocusable_SkipsToTheNextFocusableForward()
    {
        var items = new[] { "a", "skip", "c" };

        bool found = NeighborFocus.TryGetNeighborAfterRemove(
            items, removedIndex: 0, candidate => candidate != "skip", out var neighbor);

        Assert.True(found);
        Assert.Equal("c", neighbor);
    }

    [Fact]
    public void TryGetNeighborAfterRemove_NegativeRemovedIndex_WalksForwardFromTheFirstItem()
    {
        var items = new[] { "a", "b", "c" };

        bool found = NeighborFocus.TryGetNeighborAfterRemove(items, removedIndex: -1, Always, out var neighbor);

        Assert.True(found);
        Assert.Equal("a", neighbor);
    }

    [Fact]
    public void TryGetNeighborAfterRemove_NoFocusableCandidates_ReturnsFalse()
    {
        var items = new[] { "skip", "skip", "skip" };

        bool found = NeighborFocus.TryGetNeighborAfterRemove(items, removedIndex: 1, _ => false, out _);

        Assert.False(found);
    }

    [Fact]
    public void TryGetNeighborAfterRemove_NoFocusableForward_FallsBackToAFocusableBackward()
    {
        var items = new[] { "a", "b", "skip" };

        bool found = NeighborFocus.TryGetNeighborAfterRemove(
            items, removedIndex: 1, candidate => candidate != "skip", out var neighbor);

        Assert.True(found);
        Assert.Equal("a", neighbor);
    }

    [Fact]
    public void TryGetNeighborAfterRemove_RemovedIndexPastEnd_WalksBackwardFromTheLastItem()
    {
        var items = new[] { "a", "b", "c" };

        bool found = NeighborFocus.TryGetNeighborAfterRemove(items, removedIndex: items.Length, Always, out var neighbor);

        Assert.True(found);
        Assert.Equal("c", neighbor);
    }

    [Fact]
    public void TryGetNeighborAfterRemove_RemovingFirst_PicksTheNextItem()
    {
        var items = new[] { "a", "b", "c" };

        bool found = NeighborFocus.TryGetNeighborAfterRemove(items, removedIndex: 0, Always, out var neighbor);

        Assert.True(found);
        Assert.Equal("b", neighbor);
    }

    [Fact]
    public void TryGetNeighborAfterRemove_RemovingLast_FallsBackToThePreviousItem()
    {
        var items = new[] { "a", "b", "c" };

        bool found = NeighborFocus.TryGetNeighborAfterRemove(items, removedIndex: 2, Always, out var neighbor);

        Assert.True(found);
        Assert.Equal("b", neighbor);
    }

    [Fact]
    public void TryGetNeighborAfterRemove_RemovingMiddle_PicksTheForwardNeighbor()
    {
        var items = new[] { "a", "b", "c" };

        bool found = NeighborFocus.TryGetNeighborAfterRemove(items, removedIndex: 1, Always, out var neighbor);

        Assert.True(found);
        Assert.Equal("c", neighbor);
    }

    [Fact]
    public void TryGetNeighborAfterRemove_RemovingTheOnlyItem_ReturnsFalse()
    {
        var items = new[] { "a" };

        bool found = NeighborFocus.TryGetNeighborAfterRemove(items, removedIndex: 0, Always, out _);

        Assert.False(found);
    }

    [Fact]
    public void TryGetNeighborAfterRemove_ValueTypeKeys_SignalsAbsenceViaBoolNotDefault()
    {
        // A value-type T must not be mistaken for a found neighbor: the bool return is the only signal. Guid.Empty is
        // the default out value, yet "not found" stays false rather than surfacing default(Guid) as a hit.
        var items = new[] { Guid.NewGuid() };

        bool found = NeighborFocus.TryGetNeighborAfterRemove(items, removedIndex: 0, _ => true, out var neighbor);

        Assert.False(found);
        Assert.Equal(default, neighbor);
    }
}
