// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;

namespace EventLogExpert.Runtime.Tests.LogTable.OrderedView;

public sealed class ViewContentTokenTests
{
    private static readonly EventLogId s_logA = EventLogId.Create();
    private static readonly EventLogId s_logB = EventLogId.Create();

    [Fact]
    public void AddingAMember_IsNotEqual()
    {
        ViewContentToken single = ViewContentToken.FromStamps(default, [Stamp(s_logA, 1, 3)], 3);
        ViewContentToken combined = ViewContentToken.FromStamps(default, [Stamp(s_logA, 1, 3), Stamp(s_logB, 2, 4)], 7);

        Assert.NotEqual(single, combined);
    }

    [Fact]
    public void DifferentContentVersion_IsNotEqual()
    {
        ViewContentToken before = ViewContentToken.FromStamps(default, [Stamp(s_logA, contentVersion: 5, count: 10)], 8);
        ViewContentToken after = ViewContentToken.FromStamps(default, [Stamp(s_logA, contentVersion: 6, count: 10)], 8);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void DifferentFilter_IsNotEqual()
    {
        var enabled = new Filter(new DateFilter { IsEnabled = true }, []);
        var disabled = new Filter(new DateFilter { IsEnabled = false }, []);

        ViewContentToken filtered = ViewContentToken.FromStamps(enabled, [Stamp(s_logA, 1, 3)], 3);
        ViewContentToken unfiltered = ViewContentToken.FromStamps(disabled, [Stamp(s_logA, 1, 3)], 3);

        Assert.NotEqual(filtered, unfiltered);
    }

    [Fact]
    public void DifferentSurvivorCount_IsNotEqual()
    {
        ViewContentToken fewer = ViewContentToken.FromStamps(default, [Stamp(s_logA, contentVersion: 5, count: 10)], 4);
        ViewContentToken more = ViewContentToken.FromStamps(default, [Stamp(s_logA, contentVersion: 5, count: 10)], 8);

        Assert.NotEqual(fewer, more);
    }

    [Fact]
    public void Empty_EqualsEmpty_ButNotContent()
    {
        Assert.Equal(ViewContentToken.Empty, ViewContentToken.Empty);
        Assert.True(default(ViewContentToken) == ViewContentToken.Empty);

        ViewContentToken content = ViewContentToken.FromStamps(default, [Stamp(s_logA, 1, 3)], 3);
        Assert.NotEqual(ViewContentToken.Empty, content);
        Assert.True(ViewContentToken.Empty != content);
    }

    [Fact]
    public void From_OverARealSnapshot_MatchesTheStampsOfItsInScopeReaders()
    {
        EventLogId logId = EventLogId.Create();
        IEventColumnReader reader = EventColumnStore
            .Build(BuildEvents(8), generation: 0, contentVersion: 0)
            .CreateReader(logId);

        var state = new OrderedViewState();
        state.ReconcileLog(logId, reader);
        RebuildRequest request = state.BeginRebuild(
            static (_, _) => true, new SortContext(orderBy: null, isDescending: false, groupBy: null, isGroupDescending: false));
        Assert.True(state.TryAdoptRebuild(
            request, OrderedViewState.BuildIndex(request, TestContext.Current.CancellationToken)));

        OrderedViewSnapshot snapshot = state.Current;
        var filter = new Filter(new DateFilter { IsEnabled = true }, []);

        ViewContentToken fromSnapshot = ViewContentToken.From(filter, state.AdoptedInScope, snapshot);
        ViewContentToken fromStamps = ViewContentToken.FromStamps(
            filter,
            [new ViewContentTokenReaderStamp(logId, reader.Generation, reader.ContentVersion, reader.Count)],
            snapshot.Count);

        Assert.Equal(fromStamps, fromSnapshot);
        Assert.NotEqual(ViewContentToken.Empty, fromSnapshot);
    }

    [Fact]
    public void MemberOrder_DoesNotMatter()
    {
        ViewContentToken oneOrder = ViewContentToken.FromStamps(
            default, [Stamp(s_logA, 1, 3), Stamp(s_logB, 2, 4)], 7);
        ViewContentToken otherOrder = ViewContentToken.FromStamps(
            default, [Stamp(s_logB, 2, 4), Stamp(s_logA, 1, 3)], 7);

        Assert.Equal(oneOrder, otherOrder);
    }

    [Fact]
    public void SameStamps_AreEqual()
    {
        ViewContentToken first = ViewContentToken.FromStamps(default, [Stamp(s_logA, contentVersion: 5, count: 10)], 8);
        ViewContentToken second = ViewContentToken.FromStamps(default, [Stamp(s_logA, contentVersion: 5, count: 10)], 8);

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    private static ResolvedEvent[] BuildEvents(int count)
    {
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var events = new ResolvedEvent[count];

        for (int i = 0; i < count; i++)
        {
            events[i] = new ResolvedEvent("Application", LogPathType.Channel)
            {
                RecordId = i + 1,
                TimeCreated = baseTime.AddSeconds(i),
                Id = 1000 + i,
                Source = "Source"
            };
        }

        return events;
    }

    private static ViewContentTokenReaderStamp Stamp(EventLogId logId, long contentVersion, int count) =>
        new(logId, Generation: 0, contentVersion, count);
}
