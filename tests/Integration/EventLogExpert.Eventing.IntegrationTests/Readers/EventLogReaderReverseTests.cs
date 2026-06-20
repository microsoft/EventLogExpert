// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.TestUtils.Constants;

namespace EventLogExpert.Eventing.IntegrationTests.Readers;

public sealed class EventLogReaderReverseTests
{
    [Fact]
    public void ForwardRead_NewestBookmark_ShouldAliasLastBookmark()
    {
        using var fixture = new SmallEvtxFixture();
        using var reader = new EventLogReader(fixture.FilePath, LogPathType.File);

        while (reader.TryGetEvents(out _)) { }

        Assert.NotNull(reader.LastBookmark);

        // Forward never captures a separate newest bookmark; NewestBookmark returns LastBookmark directly.
        Assert.Equal(reader.LastBookmark, reader.NewestBookmark);
    }

    [Fact]
    public void ForwardRead_ShouldReturnRecordIdsAscending()
    {
        using var fixture = new SmallEvtxFixture();
        using var reader = new EventLogReader(fixture.FilePath, LogPathType.File);

        var ids = ReadAllRecordIds(reader, batchSize: 1);

        Assert.True(ids.Count >= 2, "fixture should export at least two events");
        AssertStrictlyOrdered(ids, ascending: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewestBookmark_WhenInitialized_ShouldBeNull(bool reverseDirection)
    {
        using var reader = new EventLogReader(Constants.ApplicationLogName, LogPathType.Channel, reverseDirection: reverseDirection);

        Assert.Null(reader.NewestBookmark);
    }

    [Fact]
    public void NewestEvent_ShouldBeIdenticalRegardlessOfDirection()
    {
        using var fixture = new SmallEvtxFixture();
        using var forward = new EventLogReader(fixture.FilePath, LogPathType.File);
        using var reverse = new EventLogReader(fixture.FilePath, LogPathType.File, reverseDirection: true);

        var forwardIds = ReadAllRecordIds(forward, batchSize: 30);
        var reverseIds = ReadAllRecordIds(reverse, batchSize: 30);

        Assert.NotEmpty(forwardIds);
        Assert.NotEmpty(reverseIds);

        // The newest event is the last one read forward and the first one read reverse (same RecordId).
        Assert.Equal(forwardIds[^1], reverseIds[0]);
        Assert.Equal(forwardIds.Max(), reverseIds.Max());
        Assert.Equal(forwardIds.Min(), reverseIds.Min());
    }

    [Fact]
    public void ReverseRead_NewestBookmark_ShouldBeStableAcrossBatches()
    {
        using var fixture = new SmallEvtxFixture();
        using var reader = new EventLogReader(fixture.FilePath, LogPathType.File, reverseDirection: true);

        Assert.True(reader.TryGetEvents(out var firstBatch, 1));
        Assert.Single(firstBatch);

        string? newestAfterFirstBatch = reader.NewestBookmark;
        Assert.NotNull(newestAfterFirstBatch);

        while (reader.TryGetEvents(out _, 1)) { }

        // The newest bookmark is captured once on the first batch and must not drift to a later (older) batch.
        Assert.Equal(newestAfterFirstBatch, reader.NewestBookmark);
    }

    [Fact]
    public void ReverseRead_NewestBookmark_ShouldDifferFromLastBookmark()
    {
        using var fixture = new SmallEvtxFixture();
        using var reader = new EventLogReader(fixture.FilePath, LogPathType.File, reverseDirection: true);

        while (reader.TryGetEvents(out _, 1)) { }

        Assert.NotNull(reader.NewestBookmark);
        Assert.NotNull(reader.LastBookmark);

        // For a multi-event log read newest-first, NewestBookmark (newest) and LastBookmark (oldest enumerated) differ.
        Assert.NotEqual(reader.NewestBookmark, reader.LastBookmark);
    }

    [Fact]
    public void ReverseRead_OnLiveChannel_FirstBatchShouldBeNewestFirst()
    {
        using var reader = new EventLogReader(Constants.ApplicationLogName, LogPathType.Channel, reverseDirection: true);

        Assert.True(reader.TryGetEvents(out var batch, 10));

        var ids = batch.Where(evt => evt.RecordId is not null).Select(evt => evt.RecordId!.Value).ToList();

        Assert.NotEmpty(ids);
        AssertStrictlyOrdered(ids, ascending: false);
        Assert.NotNull(reader.NewestBookmark);
    }

    [Fact]
    public void ReverseRead_ShouldReturnRecordIdsDescendingAcrossBatches()
    {
        using var fixture = new SmallEvtxFixture();
        using var reader = new EventLogReader(fixture.FilePath, LogPathType.File, reverseDirection: true);

        // batchSize 1 forces every event into its own batch, so this also proves cross-batch monotonicity
        // (the last RecordId of batch N is greater than the first RecordId of batch N+1).
        var ids = ReadAllRecordIds(reader, batchSize: 1);

        Assert.True(ids.Count >= 2, "fixture should export at least two events");
        AssertStrictlyOrdered(ids, ascending: false);
    }

    [Fact]
    public void ReverseRead_SingleEventFirstBatch_ShouldCaptureNewestBookmarkWithoutCrashing()
    {
        using var fixture = new SmallEvtxFixture();
        using var reader = new EventLogReader(fixture.FilePath, LogPathType.File, reverseDirection: true);

        Assert.True(reader.TryGetEvents(out var batch, 1));
        Assert.Single(batch);

        // buffer[0] == buffer[count - 1] here: both bookmarks are taken from the same single (newest) handle,
        // each wrapped non-owning, so the double-wrap must not double-free or crash.
        Assert.NotNull(reader.NewestBookmark);
        Assert.NotNull(reader.LastBookmark);
    }

    [Fact]
    public void ReverseRead_WhenInvalidLog_ShouldFailWithoutBookmark()
    {
        using var reader = new EventLogReader("NonExistentLog_" + Guid.NewGuid(), LogPathType.Channel, reverseDirection: true);

        Assert.False(reader.TryGetEvents(out var events));
        Assert.Empty(events);
        Assert.Null(reader.NewestBookmark);
    }

    private static void AssertStrictlyOrdered(IReadOnlyList<long> ids, bool ascending)
    {
        for (int i = 1; i < ids.Count; i++)
        {
            bool ordered = ascending ? ids[i] > ids[i - 1] : ids[i] < ids[i - 1];

            Assert.True(
                ordered,
                $"RecordIds not strictly {(ascending ? "ascending" : "descending")} at index {i}: {ids[i - 1]} then {ids[i]}");
        }
    }

    private static List<long> ReadAllRecordIds(EventLogReader reader, int batchSize)
    {
        var ids = new List<long>();

        while (reader.TryGetEvents(out var batch, batchSize))
        {
            foreach (var evt in batch)
            {
                if (evt.RecordId is { } id) { ids.Add(id); }
            }
        }

        return ids;
    }
}
