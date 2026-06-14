// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using System.Diagnostics;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class SegmentedSortedListTests
{
    private static readonly SortContext s_byTime = new(ColumnName.DateAndTime, isDescending: false, groupBy: null, isGroupDescending: false);

    private static readonly Comparison<ResolvedEvent> s_comparer =
        ResolvedEventOrdering.SelectComparer(ColumnName.DateAndTime, false, null, false);

    [Fact]
    public void Append_BatchEntirelyAfter_Appends()
    {
        var existing = SegmentedSortedList.CreateSorted([Ev("LogA", 100, 1), Ev("LogA", 110, 2)], s_byTime);
        var later = new[] { Ev("LogA", 200, 3), Ev("LogA", 210, 4) };

        var result = existing.Append(later);

        AssertSameSequence(StableMerge(existing, later), result);
    }

    [Fact]
    public void Append_BatchEntirelyBefore_Prepends()
    {
        var existing = SegmentedSortedList.CreateSorted([Ev("LogA", 100, 1), Ev("LogA", 110, 2)], s_byTime);
        var earlier = new[] { Ev("LogA", 10, 3), Ev("LogA", 20, 4) };

        var result = existing.Append(earlier);

        AssertSameSequence(StableMerge(existing, earlier), result);
    }

    [Fact]
    public void Append_BatchTiesWithExistingHead_KeepsExistingFirst()
    {
        var head = Ev("LogA", 100, null);
        var existing = SegmentedSortedList.CreateSorted([head, Ev("LogA", 200, 2), Ev("LogA", 300, 3)], s_byTime);
        var tied = Ev("LogA", 100, null);

        var result = existing.Append([tied]);

        AssertSameSequence(StableMerge(existing, [tied]), result);
        Assert.Same(head, result[0]);
        Assert.Same(tied, result[1]);
    }

    [Fact]
    public void Append_BatchTiesWithExistingTail_KeepsExistingBeforeBatch()
    {
        var tail = Ev("LogA", 200, null);
        var existing = SegmentedSortedList.CreateSorted([Ev("LogA", 100, 1), tail], s_byTime);
        var tied = Ev("LogA", 200, null);

        var result = existing.Append([tied]);

        AssertSameSequence(StableMerge(existing, [tied]), result);
        Assert.Same(tail, result[1]);
        Assert.Same(tied, result[2]);
    }

    [Fact]
    public void Append_InterleaveThenPrepend_StaysSortedAndCorrect()
    {
        var existing = SegmentedSortedList.CreateSorted([Ev("LogA", 100, 1), Ev("LogA", 300, 3)], s_byTime);
        var interleaving = new[] { Ev("LogA", 200, 2) };
        var prepend = new[] { Ev("LogA", 1, 0) };

        var afterMerge = existing.Append(interleaving);
        var result = afterMerge.Append(prepend);

        AssertSameSequence(StableMerge(StableMerge(existing, interleaving), prepend), result);
        AssertSorted(result);
    }

    [Fact]
    public void Append_InterleavingBatch_MatchesStableMerge()
    {
        var existing = SegmentedSortedList.CreateSorted([Ev("LogA", 100, 1), Ev("LogA", 300, 3)], s_byTime);
        var interleaving = new[] { Ev("LogA", 200, 2) };

        var result = existing.Append(interleaving);

        AssertSameSequence(StableMerge(existing, interleaving), result);
    }

    [Fact]
    public void Append_InterleavingMultiLogBatches_StayCorrectlySorted()
    {
        var list = SegmentedSortedList.CreateSorted([Ev("LogA", 100, 1), Ev("LogB", 150, 2)], s_byTime);

        list = list.Append([Ev("LogA", 120, 3), Ev("LogB", 90, 4)]);
        list = list.Append([Ev("LogA", 80, 5), Ev("LogB", 200, 6)]);

        Assert.Equal(6, list.Count);
        AssertSorted(list);
    }

    [Fact]
    public void Append_SingleLogDescendingStream_UsesPrependFastPathWithoutCopying()
    {
        var descending = new SortContext(ColumnName.DateAndTime, isDescending: true, groupBy: null, isGroupDescending: false);
        var comparer = ResolvedEventOrdering.SelectComparer(ColumnName.DateAndTime, true, null, false);

        var list = SegmentedSortedList.CreateSorted([], descending);

        for (int batch = 0; batch < 20; batch++)
        {
            list = list.Append([Ev("LogA", 1000 + batch, batch + 1)]);
        }

        Assert.Equal(20, list.Count);
        Assert.Equal(20, list.SegmentCount);
        Assert.Equal(20L, list[0].RecordId);
        Assert.Equal(1L, list[^1].RecordId);

        for (int i = 1; i < list.Count; i++)
        {
            Assert.True(comparer(list[i - 1], list[i]) <= 0);
        }
    }

    [Fact]
    public void CopyTo_CopiesAllElementsInEnumerationOrder()
    {
        var list = SegmentedSortedList.CreateSorted([Ev("LogA", 100, 1), Ev("LogA", 110, 2)], s_byTime)
            .Append([Ev("LogA", 200, 3)])
            .Append([Ev("LogA", 10, 4)]);

        var array = new ResolvedEvent[list.Count];
        list.CopyTo(array, 0);

        AssertSameSequence(list, array);
    }

    [Fact]
    public void Implements_ListContract_ForVirtualizeBinding()
    {
        var list = SegmentedSortedList.CreateSorted([Ev("LogA", 1, 1), Ev("LogA", 2, 2)], s_byTime);

        Assert.IsAssignableFrom<IReadOnlyList<ResolvedEvent>>(list);
        Assert.IsAssignableFrom<IList<ResolvedEvent>>(list);

        var collection = list as ICollection<ResolvedEvent>;

        Assert.NotNull(collection);
        Assert.True(collection!.IsReadOnly);
        Assert.Equal(2, collection.Count);
        Assert.Throws<NotSupportedException>(() => collection.Add(Ev("LogA", 3, 3)));
        Assert.Throws<NotSupportedException>(() => collection.Clear());
        Assert.Throws<NotSupportedException>(() => collection.Remove(Ev("LogA", 1, 1)));

        var asList = (IList<ResolvedEvent>)list;

        Assert.Throws<NotSupportedException>(() => asList[0] = Ev("LogA", 9, 9));
        Assert.Throws<NotSupportedException>(() => asList.Insert(0, Ev("LogA", 9, 9)));
        Assert.Throws<NotSupportedException>(() => asList.RemoveAt(0));
    }

    [Fact]
    public void Indexer_MatchesEnumeration_AcrossSegments()
    {
        var list = SegmentedSortedList.CreateSorted([Ev("LogA", 100, 1)], s_byTime)
            .Append([Ev("LogA", 200, 2), Ev("LogA", 210, 3)])
            .Append([Ev("LogA", 1, 4), Ev("LogA", 2, 5)]);

        var enumerated = list.ToList();

        Assert.Equal(5, list.Count);

        for (int i = 0; i < list.Count; i++)
        {
            Assert.Same(enumerated[i], list[i]);
        }
    }

    [Fact]
    public void Indexer_OutOfRange_Throws()
    {
        var list = SegmentedSortedList.CreateSorted([Ev("LogA", 1, 1)], s_byTime);

        Assert.Throws<ArgumentOutOfRangeException>(() => list[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => list[-1]);
    }

    [Fact]
    public void MergeSorted_ContextDrift_ThrowsUnreachable()
    {
        var byLevel = new SortContext(ColumnName.Level, isDescending: false, groupBy: null, isGroupDescending: false);
        IReadOnlyList<ResolvedEvent> existing = SegmentedSortedList.CreateSorted(
            [Ev("LogA", 300, 1, "Critical"), Ev("LogA", 100, 2, "Warning")], byLevel);

        Assert.Throws<UnreachableException>(() =>
            ResolvedEventOrdering.MergeSorted(existing, [Ev("LogA", 200, 3, "Information")], ColumnName.DateAndTime, isDescending: false));
    }

    [Fact]
    public void MergeSorted_PublicPath_TieBoundary_MatchesStableMerge()
    {
        var head = Ev("LogA", 100, null);
        IReadOnlyList<ResolvedEvent> existing =
            SegmentedSortedList.CreateSorted([head, Ev("LogA", 200, 2)], s_byTime);
        var tied = new[] { Ev("LogA", 100, null) };

        var result = ResolvedEventOrdering.MergeSorted(existing, tied, ColumnName.DateAndTime, isDescending: false);

        AssertSameSequence(StableMerge(existing, tied), result);
        Assert.Same(head, result[0]);
    }

    [Fact]
    public void ResolvedEventIndex_IndexOf_FindsInstancesAcrossSegments_IncludingNullRecordIdTieWindow()
    {
        var e1 = Ev("LogA", 100, null);
        var e2 = Ev("LogA", 100, null);
        var list = SegmentedSortedList.CreateSorted([e1, e2], s_byTime);

        var e3 = Ev("LogA", 100, null);
        list = list.Append([e3]);

        Assert.Equal(2, list.SegmentCount);

        int i1 = ResolvedEventIndex.IndexOf(list, e1, ColumnName.DateAndTime);
        int i2 = ResolvedEventIndex.IndexOf(list, e2, ColumnName.DateAndTime);
        int i3 = ResolvedEventIndex.IndexOf(list, e3, ColumnName.DateAndTime);

        Assert.Same(e1, list[i1]);
        Assert.Same(e2, list[i2]);
        Assert.Same(e3, list[i3]);
        Assert.Equal([0, 1, 2], new[] { i1, i2, i3 }.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void WhereSegmented_DropsEmptyMiddleSegment()
    {
        var middle = SegmentedSortedList.CreateSorted([Ev("LogC", 100, 1), Ev("LogC", 110, 2)], s_byTime);
        var withLate = middle.Append([Ev("LogB", 200, 3)]);
        var threeSegments = withLate.Append([Ev("LogA", 1, 4)]);

        var filtered = threeSegments.WhereSegmented(e => !string.Equals(e.OwningLog, "LogC", StringComparison.Ordinal));

        Assert.Equal(2, filtered.Count);
        Assert.DoesNotContain(filtered, e => string.Equals(e.OwningLog, "LogC", StringComparison.Ordinal));
        AssertSorted(filtered);
    }

    private static void AssertSameSequence(IReadOnlyList<ResolvedEvent> expected, IReadOnlyList<ResolvedEvent> actual)
    {
        Assert.Equal(expected.Count, actual.Count);

        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Same(expected[i], actual[i]);
        }
    }

    private static void AssertSorted(IReadOnlyList<ResolvedEvent> events)
    {
        for (int i = 1; i < events.Count; i++)
        {
            Assert.True(s_comparer(events[i - 1], events[i]) <= 0);
        }
    }

    private static ResolvedEvent Ev(string owningLog, int timeMs, long? recordId, string level = "") =>
        new(owningLog, LogPathType.Channel)
        {
            TimeCreated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(timeMs),
            RecordId = recordId,
            Level = level
        };

    private static List<ResolvedEvent> StableMerge(IReadOnlyList<ResolvedEvent> existing, IReadOnlyList<ResolvedEvent> batch)
    {
        var sortedBatch = new List<ResolvedEvent>(batch);
        sortedBatch.Sort(s_comparer);

        var result = new List<ResolvedEvent>(existing.Count + sortedBatch.Count);
        int i = 0, j = 0;

        while (i < existing.Count && j < sortedBatch.Count)
        {
            result.Add(s_comparer(existing[i], sortedBatch[j]) <= 0 ? existing[i++] : sortedBatch[j++]);
        }

        while (i < existing.Count) { result.Add(existing[i++]); }

        while (j < sortedBatch.Count) { result.Add(sortedBatch[j++]); }

        return result;
    }
}
