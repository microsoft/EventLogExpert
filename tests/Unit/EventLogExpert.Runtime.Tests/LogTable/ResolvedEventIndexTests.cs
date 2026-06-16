// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class ResolvedEventIndexTests
{
    private static readonly DateTime s_baseTime = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void IndexOf_ClonedEventWithMatchingKey_ResolvesToItsIndex()
    {
        var events = BuildByDateAscending(6);
        var original = events[3];
        var clone = NewEvent(original.RecordId, original.TimeCreated, original.OwningLog, original.Source);

        Assert.Equal(3, ResolvedEventIndex.IndexOf(events, clone, ColumnName.DateAndTime, isDescending: false));
    }

    [Fact]
    public void IndexOf_DescendingOrder_ReturnsCorrectIndex()
    {
        var newest = NewEvent(recordId: 3, time: s_baseTime.AddSeconds(30), owningLog: "Application", source: "A");
        var middle = NewEvent(recordId: 2, time: s_baseTime.AddSeconds(20), owningLog: "Application", source: "B");
        var oldest = NewEvent(recordId: 1, time: s_baseTime.AddSeconds(10), owningLog: "Application", source: "C");

        var events = new List<ResolvedEvent> { newest, middle, oldest }.AsReadOnly();

        Assert.Equal(0, ResolvedEventIndex.IndexOf(events, newest, ColumnName.DateAndTime, isDescending: true));
        Assert.Equal(1, ResolvedEventIndex.IndexOf(events, middle, ColumnName.DateAndTime, isDescending: true));
        Assert.Equal(2, ResolvedEventIndex.IndexOf(events, oldest, ColumnName.DateAndTime, isDescending: true));
    }

    [Fact]
    public void IndexOf_EachEventInSortedList_ReturnsItsIndex()
    {
        var events = BuildByDateAscending(6);

        for (int i = 0; i < events.Count; i++)
        {
            Assert.Equal(i, ResolvedEventIndex.IndexOf(events, events[i], ColumnName.DateAndTime, isDescending: false));
        }
    }

    [Fact]
    public void IndexOf_EventNotInList_ReturnsMinusOne()
    {
        var events = BuildByDateAscending(6);
        var absent = NewEvent(recordId: 999, time: s_baseTime.AddSeconds(999), owningLog: "Application", source: "Z");

        Assert.Equal(-1, ResolvedEventIndex.IndexOf(events, absent, ColumnName.DateAndTime, isDescending: false));
    }

    [Fact]
    public void IndexOf_GroupedBySource_FindsEventInItsGroup()
    {
        var alphaFirst = NewEvent(recordId: 1, time: s_baseTime.AddSeconds(1), owningLog: "Application", source: "Alpha");
        var alphaSecond = NewEvent(recordId: 2, time: s_baseTime.AddSeconds(2), owningLog: "Application", source: "Alpha");
        var beta = NewEvent(recordId: 3, time: s_baseTime.AddSeconds(3), owningLog: "Application", source: "Beta");

        var events = new List<ResolvedEvent> { alphaFirst, alphaSecond, beta }.AsReadOnly();

        Assert.Equal(0, ResolvedEventIndex.IndexOf(events, alphaFirst, ColumnName.DateAndTime, isDescending: false, groupBy: ColumnName.Source));
        Assert.Equal(1, ResolvedEventIndex.IndexOf(events, alphaSecond, ColumnName.DateAndTime, isDescending: false, groupBy: ColumnName.Source));
        Assert.Equal(2, ResolvedEventIndex.IndexOf(events, beta, ColumnName.DateAndTime, isDescending: false, groupBy: ColumnName.Source));
    }

    [Fact]
    public void IndexOf_NullRecordIdTie_ReturnsCorrectInstanceByReference()
    {
        var tiedTime = s_baseTime.AddSeconds(10);
        var first = NewEvent(recordId: null, time: tiedTime, owningLog: "System", source: "First");
        var second = NewEvent(recordId: null, time: tiedTime, owningLog: "System", source: "Second");
        var before = NewEvent(recordId: 1, time: s_baseTime.AddSeconds(1), owningLog: "System", source: "Before");
        var after = NewEvent(recordId: 2, time: s_baseTime.AddSeconds(20), owningLog: "System", source: "After");

        var events = new List<ResolvedEvent> { before, first, second, after }.AsReadOnly();

        Assert.Equal(1, ResolvedEventIndex.IndexOf(events, first, ColumnName.DateAndTime, isDescending: false));
        Assert.Equal(2, ResolvedEventIndex.IndexOf(events, second, ColumnName.DateAndTime, isDescending: false));
    }

    [Fact]
    public void IndexOf_OrderByNull_NormalizesToDateAndTimeNotRecordId()
    {
        var first = NewEvent(recordId: 30, time: s_baseTime.AddSeconds(1), owningLog: "Application", source: "A");
        var second = NewEvent(recordId: 20, time: s_baseTime.AddSeconds(2), owningLog: "Application", source: "B");
        var third = NewEvent(recordId: 10, time: s_baseTime.AddSeconds(3), owningLog: "Application", source: "C");

        var events = new List<ResolvedEvent> { first, second, third }.AsReadOnly();

        Assert.Equal(0, ResolvedEventIndex.IndexOf(events, first, orderBy: null, isDescending: false));
        Assert.Equal(1, ResolvedEventIndex.IndexOf(events, second, orderBy: null, isDescending: false));
        Assert.Equal(2, ResolvedEventIndex.IndexOf(events, third, orderBy: null, isDescending: false));
    }

    private static IReadOnlyList<ResolvedEvent> BuildByDateAscending(int count)
    {
        var events = new List<ResolvedEvent>(count);

        for (int i = 0; i < count; i++)
        {
            events.Add(NewEvent(i + 1, s_baseTime.AddSeconds(i + 1), "Application", $"Source{i}"));
        }

        return events.AsReadOnly();
    }

    private static ResolvedEvent NewEvent(long? recordId, DateTime time, string owningLog, string source) =>
        new(owningLog, LogPathType.Channel)
        {
            RecordId = recordId,
            TimeCreated = time,
            Source = source,
            LogName = owningLog
        };
}
