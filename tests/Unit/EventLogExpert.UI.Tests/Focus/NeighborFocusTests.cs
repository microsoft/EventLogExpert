// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Focus;

namespace EventLogExpert.UI.Tests.Focus;

public sealed class NeighborFocusTests
{
    private static readonly Func<string, bool> Always = _ => true;

    [Fact]
    public void GetFallbackNeighbors_OrdersNearestForwardThenNearestBackward()
    {
        var items = new[] { "a", "b", "c", "d" };

        var neighbors = NeighborFocus.GetFallbackNeighbors(items, removedIndex: 1);

        Assert.Equal(["c", "d", "a"], neighbors);
    }

    [Fact]
    public void GetFallbackNeighbors_RemovedIndexPastEnd_ReturnsBackwardFromTheLastItem()
    {
        // The deferred/async caller records these before the removal propagates, so int.MaxValue (past the end) must
        // clamp rather than overflow: no forward candidates, backward from the last item.
        var items = new[] { "a", "b", "c" };

        var neighbors = NeighborFocus.GetFallbackNeighbors(items, int.MaxValue);

        Assert.Equal(["c", "b", "a"], neighbors);
    }

    [Fact]
    public void GetFallbackNeighbors_SoleItem_ReturnsEmpty()
    {
        var neighbors = NeighborFocus.GetFallbackNeighbors(new[] { "only" }, removedIndex: 0);

        Assert.Empty(neighbors);
    }

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
    public void TryGetNeighborAfterRemove_RemovedIndexIntMaxValue_WalksBackwardFromTheLastItem()
    {
        // int.MaxValue is past the end: removedIndex + 1 would overflow to int.MinValue and wrongly scan forward from
        // the first item, so the clamp must still select the last item via the backward walk.
        var items = new[] { "a", "b", "c" };

        bool found = NeighborFocus.TryGetNeighborAfterRemove(items, int.MaxValue, Always, out var neighbor);

        Assert.True(found);
        Assert.Equal("c", neighbor);
    }

    [Fact]
    public void TryGetNeighborAfterRemove_RemovedIndexIntMinValue_WalksForwardFromTheFirstItem()
    {
        var items = new[] { "a", "b", "c" };

        bool found = NeighborFocus.TryGetNeighborAfterRemove(items, int.MinValue, Always, out var neighbor);

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
        // Value-type T: the bool return is the only signal - "not found" must stay false, not surface default(Guid).
        var items = new[] { Guid.NewGuid() };

        bool found = NeighborFocus.TryGetNeighborAfterRemove(items, removedIndex: 0, _ => true, out var neighbor);

        Assert.False(found);
        Assert.Equal(default, neighbor);
    }
}
