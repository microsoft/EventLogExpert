// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using System.Collections.ObjectModel;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class RawEventListTests
{
    [Fact]
    public void Append_AddsEventsAtTheEndInChunkOrder()
    {
        var list = RawEventList.Empty
            .Append([Ev(1), Ev(2)])
            .Append([Ev(3)]);

        Assert.Equal(3, list.Count);
        Assert.Equal(2, list.ChunkCount);
        Assert.Equal([1, 2, 3], list.Select(e => e.Id));
    }

    [Fact]
    public void Append_And_Prepend_OfEmpty_ReturnSameInstance()
    {
        var list = RawEventList.Empty.Append([Ev(1)]);

        Assert.Same(list, list.Append([]));
        Assert.Same(list, list.Prepend([]));
    }

    [Fact]
    public void Append_IsImmutable_OriginalUnchanged()
    {
        var original = RawEventList.Empty.Append([Ev(1)]);
        var appended = original.Append([Ev(2)]);

        Assert.Single(original);
        Assert.Equal(2, appended.Count);
    }

    [Fact]
    public void Append_ReusesAReadOnlyCollectionSnapshotByReference()
    {
        // The dispatch sites pass owned immutable snapshots; a passed ReadOnlyCollection is stored as-is (no copy).
        var shared = new List<ResolvedEvent> { Ev(1), Ev(2) };
        var snapshot = new ReadOnlyCollection<ResolvedEvent>(shared);

        var list = RawEventList.Empty.Append(snapshot);

        Assert.Same(snapshot[0], list[0]);
        Assert.Same(snapshot[1], list[1]);
    }

    [Fact]
    public void Empty_HasZeroCountAndNoChunks()
    {
        Assert.Equal(0, RawEventList.Empty.ChunkCount);
        Assert.Empty(RawEventList.Empty);
    }

    [Fact]
    public void Indexer_OutOfRange_Throws()
    {
        var list = RawEventList.Empty.Append([Ev(1)]);

        Assert.Throws<ArgumentOutOfRangeException>(() => list[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => RawEventList.Empty[0]);
    }

    [Fact]
    public void Indexer_ResolvesAcrossChunkBoundaries()
    {
        var list = RawEventList.Empty
            .Append([Ev(1), Ev(2), Ev(3)])
            .Append([Ev(4)])
            .Prepend([Ev(0)]);

        // Logical order: 0 | 1,2,3 | 4
        Assert.Equal(0, list[0].Id);
        Assert.Equal(1, list[1].Id);
        Assert.Equal(3, list[3].Id);
        Assert.Equal(4, list[4].Id);
    }

    [Fact]
    public void Prepend_AddsEventsAtTheFrontNewestFirst()
    {
        var list = RawEventList.Empty
            .Append([Ev(1), Ev(2)])
            .Prepend([Ev(3)])
            .Prepend([Ev(4)]);

        // Prepend mirrors live-tail: newest chunk first, then the earlier chunks in order.
        Assert.Equal([4, 3, 1, 2], list.Select(e => e.Id));
    }

    private static ResolvedEvent Ev(int id) => new("LogA", LogPathType.Channel) { Id = id };
}
