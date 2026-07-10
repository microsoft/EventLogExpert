// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.Tests.LogTable.TestSupport;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class LegacyEventColumnViewTests
{
    private static readonly EventLogId s_logId = EventLogId.Create();

    [Fact]
    public void Count_ReflectsEvents()
    {
        var view = CreateView(Event(1, 1), Event(2, 2), Event(3, 3));

        Assert.Equal(3, view.Count);
    }

    [Fact]
    public void GetDetail_LocatorFromDifferentGeneration_Throws()
    {
        var view = CreateView(Event(1, 1));
        var stale = new EventLocator(s_logId, 999, 0);

        Assert.Throws<ArgumentException>(() => view.GetDetail(stale));
    }

    [Fact]
    public void GetDetail_ReturnsEventAtLocator()
    {
        var second = Event(20, 2);
        var view = CreateView(Event(10, 1), second, Event(30, 3));

        Assert.Same(second, view.GetDetail(view.LocatorAt(1)));
    }

    [Fact]
    public void GetDetailLean_ReturnsEventAtLocator()
    {
        var second = Event(20, 2);
        var view = CreateView(Event(10, 1), second, Event(30, 3));

        Assert.Same(second, view.GetDetailLean(view.LocatorAt(1)));
    }

    [Fact]
    public void Rank_IndexPastEnd_ReturnsMinusOne()
    {
        var view = CreateView(Event(1, 1), Event(2, 2));

        Assert.Equal(-1, view.Rank(new EventLocator(s_logId, 2, 5)));
    }

    [Fact]
    public void Rank_LocatorFromDifferentGeneration_ReturnsMinusOne()
    {
        var view = CreateView(Event(1, 1));
        var stale = new EventLocator(s_logId, 999, 0);

        Assert.Equal(-1, view.Rank(stale));
    }

    [Fact]
    public void Rank_LocatorInView_ReturnsPhysicalIndex()
    {
        var view = CreateView(Event(1, 1), Event(2, 2), Event(3, 3));

        Assert.Equal(2, view.Rank(view.LocatorAt(2)));
    }

    [Fact]
    public void Reader_ExposesSameCount()
    {
        var view = CreateView(Event(1, 1), Event(2, 2));

        Assert.Equal(view.Count, view.Reader.Count);
    }

    [Fact]
    public void Slice_PastEnd_IsEmpty()
    {
        var view = CreateView(Event(1, 1));

        Assert.Empty(view.Slice(5, 3));
    }

    [Fact]
    public void Slice_ReturnsRequestedWindow()
    {
        var view = CreateView(Event(1, 1), Event(2, 2), Event(3, 3), Event(4, 4));

        var window = view.Slice(1, 2);

        Assert.Equal(2, window.Count);
        Assert.Equal(2, window[0].Lean.Id);
        Assert.Equal(3, window[1].Lean.Id);
        Assert.Equal(view.LocatorAt(1), window[0].Loc);
        Assert.Equal(view.LocatorAt(2), window[1].Loc);
    }

    private static LegacyEventColumnView CreateView(params ResolvedEvent[] events) =>
        new(s_logId, generation: 2, contentVersion: 1, events);

    private static ResolvedEvent Event(int id, long recordId) =>
        new("live", LogPathType.Channel) { Id = id, RecordId = recordId };
}
