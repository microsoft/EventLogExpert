// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.UI.LogTable.Grouping;

namespace EventLogExpert.UI.Tests.LogTable.Grouping;

public sealed class GroupedRowViewTests
{
    [Fact]
    public void Build_WhenAllExpanded_CountIsEventsPlusGroups()
    {
        var view = Build(Events("A", "A", "B"), Collapsed());

        Assert.Equal(5, view.Count);
        Assert.Equal(2, view.Groups.Count);
    }

    [Fact]
    public void Indexer_WhenExpanded_ReturnsHeaderAtGroupStartThenEvents()
    {
        var view = Build(Events("A", "A", "B"), Collapsed());

        Assert.Equal(TableRowKind.Header, view[0].Kind);
        Assert.Equal("A", view.GroupAt(view[0]).Key);
        Assert.Equal(TableRowKind.Event, view[1].Kind);
        Assert.Equal(0, view[1].EventIndex);
        Assert.Equal(TableRowKind.Event, view[2].Kind);
        Assert.Equal(1, view[2].EventIndex);
        Assert.Equal(TableRowKind.Header, view[3].Kind);
        Assert.Equal("B", view.GroupAt(view[3]).Key);
        Assert.Equal(TableRowKind.Event, view[4].Kind);
        Assert.Equal(2, view[4].EventIndex);
    }

    [Fact]
    public void Build_WhenGroupCollapsed_HidesEventsAndReducesCount()
    {
        var view = Build(Events("A", "A", "B"), Collapsed("A"));

        Assert.Equal(3, view.Count);
        Assert.Equal(TableRowKind.Header, view[0].Kind);
        Assert.Equal("A", view.GroupAt(view[0]).Key);
        Assert.True(view.GroupAt(view[0]).IsCollapsed);
        Assert.Equal(TableRowKind.Header, view[1].Kind);
        Assert.Equal("B", view.GroupAt(view[1]).Key);
        Assert.Equal(TableRowKind.Event, view[2].Kind);
    }

    [Fact]
    public void VisibleRowForEvent_WhenExpanded_ReturnsEventRow()
    {
        var view = Build(Events("A", "A", "B"), Collapsed());

        Assert.Equal(1, view.VisibleRowForEvent(0));
        Assert.Equal(2, view.VisibleRowForEvent(1));
        Assert.Equal(4, view.VisibleRowForEvent(2));
    }

    [Fact]
    public void VisibleRowForEvent_WhenCollapsed_ReturnsParentHeaderRow()
    {
        var view = Build(Events("A", "A", "B"), Collapsed("A"));

        Assert.Equal(0, view.VisibleRowForEvent(0));
        Assert.Equal(0, view.VisibleRowForEvent(1));
        Assert.Equal(2, view.VisibleRowForEvent(2));
    }

    [Fact]
    public void GroupForEvent_ReturnsContainingGroup()
    {
        var view = Build(Events("A", "A", "B"), Collapsed());

        Assert.Equal("A", view.GroupForEvent(0).Key);
        Assert.Equal("A", view.GroupForEvent(1).Key);
        Assert.Equal("B", view.GroupForEvent(2).Key);
    }

    [Fact]
    public void VisibleRowForHeader_ReturnsHeaderVisibleRow()
    {
        var view = Build(Events("A", "A", "B"), Collapsed());

        Assert.Equal(0, view.VisibleRowForHeader("A"));
        Assert.Equal(3, view.VisibleRowForHeader("B"));
        Assert.Equal(-1, view.VisibleRowForHeader("missing"));
    }

    [Fact]
    public void TryGetGroupByKey_ReturnsGroupForKnownKeyAndFailsForMissing()
    {
        var view = Build(Events("A", "A", "B"), Collapsed());

        Assert.True(view.TryGetGroupByKey("B", out var group));
        Assert.Equal("B", group.Key);
        Assert.Equal(2, group.StartIndex);
        Assert.Equal(1, group.EventCount);

        Assert.False(view.TryGetGroupByKey("missing", out var missing));
        Assert.Equal(0, missing.EventCount);
    }

    [Fact]
    public void Build_WhenEmpty_IsEmpty()
    {
        var view = Build([], Collapsed());

        Assert.Empty(view);
        Assert.Empty(view.Groups);
    }

    [Fact]
    public void Build_WhenAllCollapsed_CountEqualsGroupCount()
    {
        var view = Build(Events("A", "A", "B", "C", "C"), Collapsed("A", "B", "C"));

        Assert.Equal(3, view.Count);
        Assert.All(view, row => Assert.Equal(TableRowKind.Header, row.Kind));
    }

    [Fact]
    public void BinarySearch_AcrossManyGroups_EveryVisibleRowResolvesCorrectly()
    {
        var sources = new List<string>();

        for (int g = 0; g < 100; g++)
        {
            sources.Add($"g{g:000}");
            sources.Add($"g{g:000}");
        }

        var view = Build(Events([.. sources]), Collapsed());

        Assert.Equal(300, view.Count);

        for (int v = 0; v < view.Count; v++)
        {
            var row = view[v];

            if (v % 3 == 0)
            {
                Assert.Equal(TableRowKind.Header, row.Kind);
                Assert.Equal(v / 3, row.GroupIndex);
            }
            else
            {
                Assert.Equal(TableRowKind.Event, row.Kind);
                Assert.Equal(view.GroupForEvent(row.EventIndex).Key, view.GroupAt(row).Key);
            }
        }
    }

    [Fact]
    public void SkipTake_ProducesCorrectWindow()
    {
        var view = Build(Events("A", "A", "B"), Collapsed());

        var window = view.Skip(1).Take(2).ToList();

        Assert.Equal(2, window.Count);
        Assert.Equal(view[1], window[0]);
        Assert.Equal(view[2], window[1]);
    }

    [Fact]
    public void IsReadOnly_AndMutatorsThrow()
    {
        var view = Build(Events("A"), Collapsed());

        Assert.True(view.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => view.Add(default));
        Assert.Throws<NotSupportedException>(() => view.Clear());
        Assert.Throws<NotSupportedException>(() => view.Insert(0, default));
        Assert.Throws<NotSupportedException>(() => view.RemoveAt(0));
        Assert.Throws<NotSupportedException>(() => view.Remove(default));
        Assert.Throws<NotSupportedException>(() => view[0] = default);
    }

    [Fact]
    public void Indexer_OutOfRange_Throws()
    {
        var view = Build(Events("A"), Collapsed());

        Assert.Throws<ArgumentOutOfRangeException>(() => view[view.Count]);
        Assert.Throws<ArgumentOutOfRangeException>(() => view[-1]);
    }

    private static GroupedRowView Build(IReadOnlyList<ResolvedEvent> events, HashSet<string> collapsed) =>
        GroupedRowView.Build(events, ColumnName.Source, collapsed.Contains);

    private static HashSet<string> Collapsed(params string[] keys) => new(keys, StringComparer.Ordinal);

    private static IReadOnlyList<ResolvedEvent> Events(params string[] sources)
    {
        var events = new List<ResolvedEvent>(sources.Length);

        for (int i = 0; i < sources.Length; i++)
        {
            events.Add(new ResolvedEvent("TestLog", LogPathType.Channel)
            {
                Id = i,
                Source = sources[i]
            });
        }

        return events;
    }
}
