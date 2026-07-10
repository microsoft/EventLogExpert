// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.UI.LogTable;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.AspNetCore.Components.Web.Virtualization;

namespace EventLogExpert.UI.Tests.LogTable;

public sealed class LogTablePaneViewportTests
{
    [Fact]
    public void ComputeEventViewport_EmptyList_ReturnsEmptyWindowAndZeroTotal()
    {
        var view = DisplayViewTestFactory.Identity([]);
        var result = LogTablePane.ComputeEventViewport(view, Request(startIndex: 0, count: 50));

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalItemCount);
    }

    [Fact]
    public void ComputeEventViewport_NormalWindow_SlicesRequestedRange()
    {
        var events = Events(10);
        var view = DisplayViewTestFactory.Identity(events);

        var result = LogTablePane.ComputeEventViewport(view, Request(startIndex: 2, count: 4));

        Assert.Equal([events[2].Id, events[3].Id, events[4].Id, events[5].Id], result.Items.Select(row => row.Lean.Id));
        Assert.Equal(10, result.TotalItemCount);
    }

    [Fact]
    public void ComputeEventViewport_RequestOverrunsTail_ClampsCountToRemaining()
    {
        var events = Events(5);
        var view = DisplayViewTestFactory.Identity(events);

        // Start at index 3 and ask for 50 rows; only indices 3 and 4 remain.
        var result = LogTablePane.ComputeEventViewport(view, Request(startIndex: 3, count: 50));

        Assert.Equal([events[3].Id, events[4].Id], result.Items.Select(row => row.Lean.Id));
        Assert.Equal(5, result.TotalItemCount);
    }

    [Fact]
    public void ComputeEventViewport_StartPastEnd_ReturnsEmptyWindowWithTrueTotal()
    {
        var events = Events(3);
        var view = DisplayViewTestFactory.Identity(events);

        var result = LogTablePane.ComputeEventViewport(view, Request(startIndex: 10, count: 50));

        Assert.Empty(result.Items);
        Assert.Equal(3, result.TotalItemCount);
    }

    [Fact]
    public void ComputeEventViewport_Window_MatchesResolvedEventIndexSlice()
    {
        var events = Events(20);
        var view = DisplayViewTestFactory.Identity(events);

        var result = LogTablePane.ComputeEventViewport(view, Request(startIndex: 7, count: 6));

        Assert.Equal(ResolvedEventIndex.Slice(events, 7, 6).Select(eventItem => eventItem.Id), result.Items.Select(row => row.Lean.Id));
    }

    [Fact]
    public void ComputeEventViewport_ZeroCountRequest_ReturnsEmptyWindow()
    {
        var events = Events(3);
        var view = DisplayViewTestFactory.Identity(events);

        var result = LogTablePane.ComputeEventViewport(view, Request(startIndex: 1, count: 0));

        Assert.Empty(result.Items);
        Assert.Equal(3, result.TotalItemCount);
    }

    private static IReadOnlyList<ResolvedEvent> Events(int count) =>
        [.. Enumerable.Range(0, count).Select(id => new ResolvedEvent("Application", LogPathType.Channel) { Id = id })];

    private static ItemsProviderRequest Request(int startIndex, int count) =>
        new(startIndex, count, CancellationToken.None);
}
