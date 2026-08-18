// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.ActivityCorrelation;
using EventLogExpert.Runtime.LogTable;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.ActivityCorrelation;

public sealed class ActivityCorrelationServiceTests
{
    private static readonly DateTime s_base = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid s_child = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid s_childTwo = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid s_focus = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly EventLogId s_logId = EventLogId.Create();
    private static readonly Guid s_parent = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid s_parentTwo = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BuildAsync_AbsentOrUnknownLevel_ContributesToNoSeverityTally()
    {
        var (service, _) = ServiceWith(1, 1,
            Event(1, s_focus, offset: 0, level: ""),
            Event(2, s_focus, offset: 1, level: "Bogus"));

        var view = await service.BuildAsync(Locator(1, 0), Ct);

        var focus = view!.Activities.Single(node => node.Role == ActivityNodeRole.Focus);
        Assert.Equal(0, focus.CriticalCount);
        Assert.Equal(0, focus.ErrorCount);
        Assert.Equal(0, focus.WarningCount);
    }

    [Fact]
    public async Task BuildAsync_BuildsParentAndChildActivitiesFromRelatedActivityId()
    {
        var (service, _) = ServiceWith(1, 1,
            Event(1, s_parent, offset: 0),
            Event(2, s_focus, s_parent, offset: 1),
            Event(3, s_focus, s_parent, offset: 2),
            Event(4, s_child, s_focus, offset: 3));

        var view = await service.BuildAsync(Locator(1, 1), Ct);

        Assert.NotNull(view);
        Assert.True(view!.HasHierarchy);

        var focus = view.Activities.Single(node => node.Role == ActivityNodeRole.Focus);
        Assert.Equal([s_parent], focus.Parents);
        Assert.Contains(view.Activities, node =>
            node.Role == ActivityNodeRole.Parent && node.ActivityId == s_parent && node.EventCount == 1);
        Assert.Contains(view.Activities, node =>
            node.Role == ActivityNodeRole.Child && node.ActivityId == s_child && node.EventCount == 1);
    }

    [Fact]
    public async Task BuildAsync_ChildNodeIncludesAllEventsOfTheChildActivity_NotOnlyTheLinkingRow()
    {
        var (service, _) = ServiceWith(1, 1,
            Event(1, s_focus, offset: 0),
            Event(2, s_child, s_focus, offset: 1),   // the child's linking (transfer) event carries RelatedActivityId
            Event(3, s_child, offset: 2),            // an ordinary child event with no RelatedActivityId link
            Event(4, s_child, offset: 3));

        var view = await service.BuildAsync(Locator(1, 0), Ct);

        var child = view!.Activities.Single(node => node.Role == ActivityNodeRole.Child);
        Assert.Equal(s_child, child.ActivityId);
        Assert.Equal(3, child.EventCount);
        Assert.Equal(3, child.Events.Count);
    }

    [Fact]
    public async Task BuildAsync_DoesNotExceedTheDisplayCapWhenRetainingTheSelectedEvent()
    {
        var events = new ResolvedEvent[260];

        for (int i = 0; i < events.Length; i++) { events[i] = Event(i, s_focus, offset: i); }

        var (service, _) = ServiceWith(1, 1, events);

        // Focus the OLDEST event (index 0): it must be retained without pushing the total past the per-activity display cap.
        var view = await service.BuildAsync(Locator(1, 0), Ct);

        var focus = view!.Activities.Single(node => node.Role == ActivityNodeRole.Focus);
        Assert.True(focus.EventsTruncated);
        Assert.Equal(200, focus.Events.Count);
        Assert.Contains(focus.Events, correlatedEvent => correlatedEvent.Locator.Index == 0);
    }

    [Fact]
    public async Task BuildAsync_DoesNotTreatASelfReferenceAsAParent()
    {
        var (service, _) = ServiceWith(1, 1, Event(1, s_focus, s_focus, offset: 0));

        var view = await service.BuildAsync(Locator(1, 0), Ct);

        var focus = view!.Activities.Single(node => node.Role == ActivityNodeRole.Focus);
        Assert.Empty(focus.Parents);
        Assert.False(view.HasHierarchy);
        Assert.Single(view.Activities);
    }

    [Fact]
    public async Task BuildAsync_ExcludesEventsWithoutActivityIdFromTheFocusGroup()
    {
        var (service, _) = ServiceWith(1, 1,
            Event(1, s_focus, offset: 0),
            Event(2, activityId: null, offset: 1),
            Event(3, s_focus, offset: 2));

        var view = await service.BuildAsync(Locator(1, 0), Ct);

        var focus = Assert.Single(view!.Activities);
        Assert.Equal(2, focus.EventCount);
    }

    [Fact]
    public async Task BuildAsync_FlagsAndCapsAnOversizedSharedActivity()
    {
        var events = new ResolvedEvent[600];

        for (int i = 0; i < events.Length; i++) { events[i] = Event(i, s_focus, offset: i); }

        var (service, _) = ServiceWith(1, 1, events);

        var view = await service.BuildAsync(Locator(1, 0), Ct);

        var focus = view!.Activities.Single(node => node.Role == ActivityNodeRole.Focus);
        Assert.True(focus.IsSharedOversized);
        Assert.True(focus.EventsTruncated);
        Assert.Equal(600, focus.EventCount);
        Assert.Equal(25, focus.Events.Count);
        Assert.Contains(focus.Events, correlatedEvent => correlatedEvent.Locator.Index == 0);
    }

    [Fact]
    public async Task BuildAsync_FlagsMultipleParents()
    {
        var (service, _) = ServiceWith(1, 1,
            Event(1, s_focus, s_parent, offset: 0),
            Event(2, s_focus, s_parentTwo, offset: 1));

        var view = await service.BuildAsync(Locator(1, 0), Ct);

        var focus = view!.Activities.Single(node => node.Role == ActivityNodeRole.Focus);
        Assert.True(focus.HasMultipleParents);
        Assert.Equal(2, focus.Parents.Count);
        Assert.Contains(s_parent, focus.Parents);
        Assert.Contains(s_parentTwo, focus.Parents);
    }

    [Fact]
    public async Task BuildAsync_GroupsFocusActivityAndFlatDegradesWithoutRelated()
    {
        var (service, _) = ServiceWith(1, 1,
            Event(1, s_focus, offset: 0),
            Event(2, s_focus, offset: 1),
            Event(3, Guid.NewGuid(), offset: 2));

        var view = await service.BuildAsync(Locator(1, 0), Ct);

        Assert.NotNull(view);
        Assert.False(view!.IsEmpty);
        Assert.False(view.HasHierarchy);
        Assert.Equal(s_focus, view.FocusActivityId);

        var focus = Assert.Single(view.Activities);
        Assert.Equal(ActivityNodeRole.Focus, focus.Role);
        Assert.Equal(2, focus.EventCount);
        Assert.Equal(2, focus.Events.Count);
        Assert.Empty(focus.Parents);
    }

    [Fact]
    public async Task BuildAsync_MutualCycle_RendersTheActivityOnceFlaggedAsCycle()
    {
        // focus <-> s_child: a focus member points at s_child (parent) and an s_child row points at focus (child).
        var (service, _) = ServiceWith(1, 1,
            Event(1, s_focus, s_child, offset: 0),
            Event(2, s_child, s_focus, offset: 1));

        var view = await service.BuildAsync(Locator(1, 0), Ct);

        var node = Assert.Single(view!.Activities, candidate => candidate.ActivityId == s_child);
        Assert.True(node.IsCycle);
        Assert.Equal(ActivityNodeRole.Parent, node.Role);
    }

    [Fact]
    public async Task BuildAsync_NodeSpanCoversAllRows_NotJustTheDisplayedEvents()
    {
        var events = new ResolvedEvent[300];

        for (int i = 0; i < events.Length; i++) { events[i] = Event(i, s_focus, offset: i); }

        var (service, _) = ServiceWith(1, 1, events);

        var view = await service.BuildAsync(Locator(1, 299), Ct);

        var focus = view!.Activities.Single(node => node.Role == ActivityNodeRole.Focus);
        Assert.True(focus.EventsTruncated);
        Assert.Equal(200, focus.Events.Count);
        Assert.Equal(299, TimeSpan.FromTicks(focus.MaxTicks - focus.MinTicks).TotalSeconds);
    }

    [Fact]
    public async Task BuildAsync_OrdersEventsNewestFirst()
    {
        var (service, _) = ServiceWith(1, 1,
            Event(1, s_focus, offset: 0),
            Event(2, s_focus, offset: 1),
            Event(3, s_focus, offset: 2));

        var view = await service.BuildAsync(Locator(1, 0), Ct);

        var focus = view!.Activities.Single(node => node.Role == ActivityNodeRole.Focus);
        Assert.Equal(3, focus.Events.Count);
        Assert.True(focus.Events[0].TimeTicks >= focus.Events[1].TimeTicks);
        Assert.True(focus.Events[1].TimeTicks >= focus.Events[2].TimeTicks);
    }

    [Fact]
    public async Task BuildAsync_OrdersRelatedActivitiesByMostRecentFirst()
    {
        var (service, _) = ServiceWith(1, 1,
            Event(1, s_focus, offset: 0),
            Event(2, s_child, s_focus, offset: 1),
            Event(3, s_childTwo, s_focus, offset: 9));

        var view = await service.BuildAsync(Locator(1, 0), Ct);

        var children = view!.Activities.Where(node => node.Role == ActivityNodeRole.Child).ToList();
        Assert.Equal(2, children.Count);
        Assert.Equal(s_childTwo, children[0].ActivityId);
        Assert.True(children[0].MaxTicks >= children[1].MaxTicks);
    }

    [Fact]
    public async Task BuildAsync_RebuildsWhenContentVersionBumpsAtTheSameGeneration()
    {
        var (service, storeState) = ServiceWith(1, 1, Event(1, s_focus, offset: 0));
        var first = await service.BuildAsync(Locator(1, 0), Ct);

        var bumped = EventColumnStore.Build([Event(1, s_focus, offset: 0)], generation: 1, contentVersion: 2);
        storeState.Value.Returns(new RawEventStoreState
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty.Add(s_logId, bumped)
        });

        var second = await service.BuildAsync(Locator(1, 0), Ct);

        Assert.NotSame(first, second);
        Assert.Equal(2, second!.Token.ContentVersion);
    }

    [Fact]
    public async Task BuildAsync_RetainsTheSelectedEventEvenWhenOlderThanTheDisplayCap()
    {
        var events = new ResolvedEvent[300];

        for (int i = 0; i < events.Length; i++) { events[i] = Event(i, s_focus, offset: i); }

        var (service, _) = ServiceWith(1, 1, events);

        var view = await service.BuildAsync(Locator(1, 0), Ct);

        var focus = view!.Activities.Single(node => node.Role == ActivityNodeRole.Focus);
        Assert.True(focus.EventsTruncated);
        Assert.Contains(focus.Events, correlatedEvent => correlatedEvent.Locator.Index == 0);
    }

    [Fact]
    public async Task BuildAsync_ReturnsNull_WhenTheLocatorGenerationIsStale()
    {
        var (service, _) = ServiceWith(2, 1, Event(1, s_focus, offset: 0));

        var view = await service.BuildAsync(Locator(1, 0), Ct);

        Assert.Null(view);
    }

    [Fact]
    public async Task BuildAsync_ReturnsNull_WhenTheLogIsNotLoaded()
    {
        var storeState = Substitute.For<IState<RawEventStoreState>>();
        storeState.Value.Returns(new RawEventStoreState());
        var service = new ActivityCorrelationService(storeState);

        var view = await service.BuildAsync(Locator(1, 0), Ct);

        Assert.Null(view);
    }

    [Fact]
    public async Task BuildAsync_ReturnsTheCachedViewForTheSameSelectionAndSnapshot()
    {
        var (service, _) = ServiceWith(1, 1, Event(1, s_focus, offset: 0));

        var first = await service.BuildAsync(Locator(1, 0), Ct);
        var second = await service.BuildAsync(Locator(1, 0), Ct);

        Assert.Same(first, second);
    }

    [Fact]
    public async Task BuildAsync_SurfacesAReferencedParentThatHasNoEventsInThisLog()
    {
        var (service, _) = ServiceWith(1, 1, Event(1, s_focus, s_parent, offset: 0));

        var view = await service.BuildAsync(Locator(1, 0), Ct);

        var parent = view!.Activities.Single(node => node.Role == ActivityNodeRole.Parent);
        Assert.Equal(s_parent, parent.ActivityId);
        Assert.Equal(0, parent.EventCount);
        Assert.Empty(parent.Events);
        Assert.True(view.HasHierarchy);
    }

    [Fact]
    public async Task BuildAsync_TalliesSeverityBeyondTheDisplayCap()
    {
        var events = new ResolvedEvent[300];

        for (int i = 0; i < events.Length; i++)
        {
            events[i] = Event(i, s_focus, offset: i, level: i % 2 == 0 ? "Error" : "Information");
        }

        var (service, _) = ServiceWith(1, 1, events);

        var view = await service.BuildAsync(Locator(1, 299), Ct);

        var focus = view!.Activities.Single(node => node.Role == ActivityNodeRole.Focus);
        Assert.Equal(150, focus.ErrorCount);
    }

    [Fact]
    public async Task BuildAsync_TalliesSeverityOverAllMemberRows()
    {
        var (service, _) = ServiceWith(1, 1,
            Event(1, s_focus, offset: 0, level: "Critical"),
            Event(2, s_focus, offset: 1, level: "Error"),
            Event(3, s_focus, offset: 2, level: "Error"),
            Event(4, s_focus, offset: 3, level: "Warning"),
            Event(5, s_focus, offset: 4, level: "Information"));

        var view = await service.BuildAsync(Locator(1, 0), Ct);

        var focus = view!.Activities.Single(node => node.Role == ActivityNodeRole.Focus);
        Assert.Equal(1, focus.CriticalCount);
        Assert.Equal(2, focus.ErrorCount);
        Assert.Equal(1, focus.WarningCount);
        Assert.Equal(3, focus.ErrorTotal);
    }

    [Fact]
    public async Task BuildAsync_WhenFocusEventHasNoActivityId_ReturnsEmptyView()
    {
        var (service, _) = ServiceWith(1, 1,
            Event(1, activityId: null, offset: 0),
            Event(2, s_focus, offset: 1));

        var view = await service.BuildAsync(Locator(1, 0), Ct);

        Assert.NotNull(view);
        Assert.True(view!.IsEmpty);
        Assert.Equal(Guid.Empty, view.FocusActivityId);
        Assert.Empty(view.Activities);
    }

    [Fact]
    public void TryGetContentToken_ReturnsTheStoreTokenAndFalseForAnAbsentLog()
    {
        var (service, _) = ServiceWith(3, 7, Event(1, s_focus, offset: 0));

        Assert.True(service.TryGetContentToken(s_logId, out var token));
        Assert.Equal(3, token.Generation);
        Assert.Equal(7, token.ContentVersion);
        Assert.Equal(1, token.Count);
        Assert.False(service.TryGetContentToken(EventLogId.Create(), out _));
    }

    private static ResolvedEvent Event(int id, Guid? activityId, Guid? relatedActivityId = null, int offset = 0, string level = "Information") =>
        new("live", LogPathType.Channel)
        {
            Id = id,
            TimeCreated = s_base.AddSeconds(offset),
            ActivityId = activityId,
            RelatedActivityId = relatedActivityId,
            Level = level,
            Source = "TestSource"
        };

    private static EventLocator Locator(int generation, int index) => new(s_logId, generation, index);

    private static (IActivityCorrelationService Service, IState<RawEventStoreState> StoreState) ServiceWith(
        int generation,
        long contentVersion,
        params ResolvedEvent[] events)
    {
        var store = EventColumnStore.Build(events, generation, contentVersion);
        var storeState = Substitute.For<IState<RawEventStoreState>>();
        storeState.Value.Returns(new RawEventStoreState
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty.Add(s_logId, store)
        });

        return (new ActivityCorrelationService(storeState), storeState);
    }
}
