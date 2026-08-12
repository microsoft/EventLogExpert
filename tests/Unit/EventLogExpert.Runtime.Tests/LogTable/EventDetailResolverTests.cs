// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class EventDetailResolverTests
{
    private const string LogName = "Application";

    [Fact]
    public void ALocatorForAClosedLog_DoesNotResolve()
    {
        var harness = new Harness(Event(1, "Alpha"));
        var locator = harness.LocatorAt(0);

        Assert.True(harness.Resolver.TryResolve(locator, out _));

        harness.CloseLog();

        Assert.False(harness.Resolver.TryResolve(locator, out var afterClose));
        Assert.Null(afterClose);
    }

    [Fact]
    public void ALocatorForALogThatWasNeverOpened_DoesNotResolve()
    {
        var harness = new Harness(Event(1, "Alpha"));

        Assert.False(harness.Resolver.TryResolve(new EventLocator(EventLogId.Create(), 0, 0), out _));
    }

    [Fact]
    public void ALocatorFromADifferentGeneration_DoesNotResolve()
    {
        var harness = new Harness(Event(1, "Alpha"));
        var mismatched = harness.LocatorAt(0) with { Generation = harness.LocatorAt(0).Generation + 1 };

        Assert.False(harness.Resolver.TryResolve(mismatched, out var detail));
        Assert.Null(detail);
    }

    [Fact]
    public void ARowHiddenByTheActiveFilter_StillResolves()
    {
        var harness = new Harness(Event(1, "Alpha"), Event(2, "Beta"));

        Assert.True(harness.Resolver.TryResolve(harness.LocatorAt(0), out var first));
        Assert.True(harness.Resolver.TryResolve(harness.LocatorAt(1), out var second));
        Assert.Equal(1, first.Id);
        Assert.Equal(2, second.Id);
    }

    [Fact]
    public void AnOutOfRangeLocator_DoesNotResolve()
    {
        var harness = new Harness(Event(1, "Alpha"));

        Assert.False(harness.Resolver.TryResolve(harness.LocatorAt(5), out _));
        Assert.False(harness.Resolver.TryResolve(harness.LocatorAt(-1), out _));
    }

    [Fact]
    public void ResolvedDetail_CarriesTheFieldsTheLeanDisplayReadOmits()
    {
        var harness = new Harness(Event(1, "Alpha") with { Xml = "<Event />" });

        Assert.True(harness.Resolver.TryResolve(harness.LocatorAt(0), out var detail));
        Assert.Equal("<Event />", detail.Xml);
    }

    private static ResolvedEvent Event(int id, string source) =>
        new(LogName, LogPathType.Channel) { Id = id, RecordId = id, Source = source };

    private sealed class Harness
    {
        private readonly IState<RawEventStoreState> _rawEventStore = Substitute.For<IState<RawEventStoreState>>();

        private RawEventStoreState _state;

        public Harness(params ResolvedEvent[] events)
        {
            _state = new RawEventStoreState
            {
                ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty
                    .Add(LogId, EventColumnStore.Build(events, 0, 0))
            };

            _rawEventStore.Value.Returns(_ => _state);
            Resolver = new EventDetailResolver(_rawEventStore);
        }

        public EventLogId LogId { get; } = EventLogId.Create();

        public EventDetailResolver Resolver { get; }

        public void CloseLog() => _state = new RawEventStoreState();

        public EventLocator LocatorAt(int index) => new(LogId, 0, index);
    }
}
