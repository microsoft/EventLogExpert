// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Store.StatusBar;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.Store;

public sealed class StatusBarStateTests
{
    [Fact]
    public void StatusBarState_DefaultState_ShouldHaveEmptyEventsLoading()
    {
        var state = new StatusBarState();

        Assert.Empty(state.EventsLoading);
    }

    [Fact]
    public void StatusBarState_DefaultState_ShouldHaveEmptyResolverStatus()
    {
        var state = new StatusBarState();

        Assert.Equal(string.Empty, state.ResolverStatus);
    }
}

public sealed class StatusBarActionTests
{
    [Fact]
    public void ClearStatusAction_ShouldCreateAction()
    {
        var activityId = Guid.NewGuid();
        var action = new StatusBarAction.ClearStatus(activityId);

        Assert.Equal(activityId, action.ActivityId);
    }

    [Fact]
    public void CloseAllAction_ShouldCreateAction()
    {
        var action = new StatusBarAction.CloseAll();

        Assert.NotNull(action);
    }

    [Fact]
    public void SetEventsLoadingAction_ShouldCreateAction()
    {
        var activityId = Guid.NewGuid();
        var count = 100;
        var failedCount = 5;
        var action = new StatusBarAction.SetEventsLoading(activityId, count, failedCount);

        Assert.Equal(activityId, action.ActivityId);
        Assert.Equal(count, action.Count);
        Assert.Equal(failedCount, action.FailedCount);
    }

    [Fact]
    public void SetResolverStatusAction_ShouldCreateAction()
    {
        var status = "Resolving events...";
        var action = new StatusBarAction.SetResolverStatus(status);

        Assert.Equal(status, action.ResolverStatus);
    }
}

public sealed class StatusBarReducerTests
{
    [Fact]
    public void ReduceClearStatus_WithEmptyState_ShouldReturnEmptyState()
    {
        var state = new StatusBarState();
        var action = new StatusBarAction.ClearStatus(Guid.NewGuid());

        var result = StatusBarReducers.ReduceClearStatus(state, action);

        Assert.Empty(result.EventsLoading);
    }

    [Fact]
    public void ReduceClearStatus_WithExistingActivity_ShouldRemoveActivity()
    {
        var activityId = Guid.NewGuid();

        var state = new StatusBarState
        {
            EventsLoading = ImmutableDictionary<Guid, (int, int)>.Empty.Add(activityId, (100, 5))
        };

        var action = new StatusBarAction.ClearStatus(activityId);

        var result = StatusBarReducers.ReduceClearStatus(state, action);

        Assert.Empty(result.EventsLoading);
    }

    [Fact]
    public void ReduceClearStatus_WithNonExistingActivity_ShouldReturnStateWithoutChange()
    {
        var existingActivityId = Guid.NewGuid();
        var nonExistingActivityId = Guid.NewGuid();

        var state = new StatusBarState
        {
            EventsLoading = ImmutableDictionary<Guid, (int, int)>.Empty.Add(existingActivityId, (100, 5))
        };

        var action = new StatusBarAction.ClearStatus(nonExistingActivityId);

        var result = StatusBarReducers.ReduceClearStatus(state, action);

        Assert.Single(result.EventsLoading);
        Assert.True(result.EventsLoading.ContainsKey(existingActivityId));
    }

    [Fact]
    public void ReduceCloseAll_ShouldReturnNewDefaultState()
    {
        var state = new StatusBarState
        {
            EventsLoading = ImmutableDictionary<Guid, (int, int)>.Empty
                .Add(Guid.NewGuid(), (100, 5))
                .Add(Guid.NewGuid(), (200, 10)),
            ResolverStatus = "Processing..."
        };

        var result = StatusBarReducers.ReduceCloseAll(state);

        Assert.Empty(result.EventsLoading);
        Assert.Equal(string.Empty, result.ResolverStatus);
    }

    [Fact]
    public void ReduceSetEventsLoading_WithExistingActivity_ShouldUpdateActivity()
    {
        var activityId = Guid.NewGuid();

        var state = new StatusBarState
        {
            EventsLoading = ImmutableDictionary<Guid, (int, int)>.Empty.Add(activityId, (50, 2))
        };

        var action = new StatusBarAction.SetEventsLoading(activityId, 100, 5);

        var result = StatusBarReducers.ReduceSetEventsLoading(state, action);

        Assert.Single(result.EventsLoading);
        Assert.Equal((100, 5), result.EventsLoading[activityId]);
    }

    [Fact]
    public void ReduceSetEventsLoading_WithMultipleActivities_ShouldMaintainOthers()
    {
        var activityId1 = Guid.NewGuid();
        var activityId2 = Guid.NewGuid();
        var activityId3 = Guid.NewGuid();

        var state = new StatusBarState
        {
            EventsLoading = ImmutableDictionary<Guid, (int, int)>.Empty
                .Add(activityId1, (100, 5))
                .Add(activityId2, (200, 10))
        };

        var action = new StatusBarAction.SetEventsLoading(activityId3, 300, 15);

        var result = StatusBarReducers.ReduceSetEventsLoading(state, action);

        Assert.Equal(3, result.EventsLoading.Count);
        Assert.True(result.EventsLoading.ContainsKey(activityId1));
        Assert.True(result.EventsLoading.ContainsKey(activityId2));
        Assert.True(result.EventsLoading.ContainsKey(activityId3));
    }

    [Fact]
    public void ReduceSetEventsLoading_WithNewActivity_ShouldAddActivity()
    {
        var state = new StatusBarState();
        var activityId = Guid.NewGuid();
        var action = new StatusBarAction.SetEventsLoading(activityId, 100, 5);

        var result = StatusBarReducers.ReduceSetEventsLoading(state, action);

        Assert.Single(result.EventsLoading);
        Assert.True(result.EventsLoading.ContainsKey(activityId));
        Assert.Equal((100, 5), result.EventsLoading[activityId]);
    }

    [Fact]
    public void ReduceSetEventsLoading_WithZeroCount_ShouldRemoveActivity()
    {
        var activityId = Guid.NewGuid();

        var state = new StatusBarState
        {
            EventsLoading = ImmutableDictionary<Guid, (int, int)>.Empty.Add(activityId, (100, 5))
        };

        var action = new StatusBarAction.SetEventsLoading(activityId, 0, 0);

        var result = StatusBarReducers.ReduceSetEventsLoading(state, action);

        Assert.Empty(result.EventsLoading);
    }

    [Fact]
    public void ReduceSetResolverStatus_ShouldReplaceExistingStatus()
    {
        var state = new StatusBarState { ResolverStatus = "Old status" };
        var newStatus = "New status";
        var action = new StatusBarAction.SetResolverStatus(newStatus);

        var result = StatusBarReducers.ReduceSetResolverStatus(state, action);

        Assert.Equal(newStatus, result.ResolverStatus);
    }

    [Fact]
    public void ReduceSetResolverStatus_ShouldSetStatus()
    {
        var state = new StatusBarState();
        var status = "Resolving 50 events...";
        var action = new StatusBarAction.SetResolverStatus(status);

        var result = StatusBarReducers.ReduceSetResolverStatus(state, action);

        Assert.Equal(status, result.ResolverStatus);
    }

    [Fact]
    public void ReduceSetResolverStatus_WithEmptyString_ShouldClearStatus()
    {
        var state = new StatusBarState { ResolverStatus = "Processing..." };
        var action = new StatusBarAction.SetResolverStatus(string.Empty);

        var result = StatusBarReducers.ReduceSetResolverStatus(state, action);

        Assert.Equal(string.Empty, result.ResolverStatus);
    }
}

public sealed class StatusBarIntegrationTests
{
    [Fact]
    public void ActivityLifecycle_ShouldHandleAddUpdateRemove()
    {
        var state = new StatusBarState();
        var activityId = Guid.NewGuid();

        state = StatusBarReducers.ReduceSetEventsLoading(
            state,
            new StatusBarAction.SetEventsLoading(activityId, 100, 0));

        Assert.Single(state.EventsLoading);
        Assert.Equal((100, 0), state.EventsLoading[activityId]);

        state = StatusBarReducers.ReduceSetEventsLoading(
            state,
            new StatusBarAction.SetEventsLoading(activityId, 75, 2));

        Assert.Single(state.EventsLoading);
        Assert.Equal((75, 2), state.EventsLoading[activityId]);

        state = StatusBarReducers.ReduceSetEventsLoading(
            state,
            new StatusBarAction.SetEventsLoading(activityId, 50, 5));

        Assert.Single(state.EventsLoading);
        Assert.Equal((50, 5), state.EventsLoading[activityId]);

        state = StatusBarReducers.ReduceClearStatus(
            state,
            new StatusBarAction.ClearStatus(activityId));

        Assert.Empty(state.EventsLoading);
    }

    [Fact]
    public void ClearNonExistingActivities_ShouldNotAffectOthers()
    {
        var activity1 = Guid.NewGuid();
        var activity2 = Guid.NewGuid();
        var nonExisting = Guid.NewGuid();

        var state = new StatusBarState
        {
            EventsLoading = ImmutableDictionary<Guid, (int, int)>.Empty
                .Add(activity1, (100, 5))
                .Add(activity2, (200, 10))
        };

        state = StatusBarReducers.ReduceClearStatus(state, new StatusBarAction.ClearStatus(nonExisting));

        Assert.Equal(2, state.EventsLoading.Count);
        Assert.True(state.EventsLoading.ContainsKey(activity1));
        Assert.True(state.EventsLoading.ContainsKey(activity2));
    }

    [Fact]
    public void CloseAll_ShouldResetAllState()
    {
        var state = new StatusBarState
        {
            EventsLoading = ImmutableDictionary<Guid, (int, int)>.Empty
                .Add(Guid.NewGuid(), (100, 5))
                .Add(Guid.NewGuid(), (200, 10))
                .Add(Guid.NewGuid(), (300, 15)),
            ResolverStatus = "Processing multiple activities..."
        };

        state = StatusBarReducers.ReduceCloseAll(state);

        Assert.Empty(state.EventsLoading);
        Assert.Equal(string.Empty, state.ResolverStatus);
    }

    [Fact]
    public void CompleteWorkflow_ShouldHandleLoadingAndResolution()
    {
        var state = new StatusBarState();
        var activityId = Guid.NewGuid();

        state = StatusBarReducers.ReduceSetEventsLoading(
            state,
            new StatusBarAction.SetEventsLoading(activityId, 100, 0));

        Assert.Single(state.EventsLoading);

        state = StatusBarReducers.ReduceSetResolverStatus(
            state,
            new StatusBarAction.SetResolverStatus("Resolving events..."));

        Assert.Equal("Resolving events...", state.ResolverStatus);
        Assert.Empty(state.EventsLoading);

        state = StatusBarReducers.ReduceSetEventsLoading(
            state,
            new StatusBarAction.SetEventsLoading(activityId, 0, 0));

        Assert.Empty(state.EventsLoading);
        Assert.Equal("Resolving events...", state.ResolverStatus);
    }

    [Fact]
    public void LoadingProgress_ShouldTrackMultipleActivities()
    {
        var state = new StatusBarState();
        var activity1 = Guid.NewGuid();
        var activity2 = Guid.NewGuid();

        state = StatusBarReducers.ReduceSetEventsLoading(
            state,
            new StatusBarAction.SetEventsLoading(activity1, 100, 0));

        Assert.Single(state.EventsLoading);

        state = StatusBarReducers.ReduceSetEventsLoading(
            state,
            new StatusBarAction.SetEventsLoading(activity2, 200, 5));

        Assert.Equal(2, state.EventsLoading.Count);

        state = StatusBarReducers.ReduceSetEventsLoading(
            state,
            new StatusBarAction.SetEventsLoading(activity1, 50, 0));

        Assert.Equal(2, state.EventsLoading.Count);
        Assert.Equal((50, 0), state.EventsLoading[activity1]);

        state = StatusBarReducers.ReduceClearStatus(state, new StatusBarAction.ClearStatus(activity1));
        Assert.Single(state.EventsLoading);
        Assert.True(state.EventsLoading.ContainsKey(activity2));
    }

    [Fact]
    public void MultipleActivitiesWithFailures_ShouldTrackIndependently()
    {
        var state = new StatusBarState();
        var activity1 = Guid.NewGuid();
        var activity2 = Guid.NewGuid();
        var activity3 = Guid.NewGuid();

        state = StatusBarReducers.ReduceSetEventsLoading(
            state,
            new StatusBarAction.SetEventsLoading(activity1, 100, 0));

        state = StatusBarReducers.ReduceSetEventsLoading(
            state,
            new StatusBarAction.SetEventsLoading(activity2, 200, 10));

        state = StatusBarReducers.ReduceSetEventsLoading(
            state,
            new StatusBarAction.SetEventsLoading(activity3, 150, 5));

        Assert.Equal(3, state.EventsLoading.Count);
        Assert.Equal((100, 0), state.EventsLoading[activity1]);
        Assert.Equal((200, 10), state.EventsLoading[activity2]);
        Assert.Equal((150, 5), state.EventsLoading[activity3]);
    }

    [Fact]
    public void ResolverStatus_ShouldUpdateIndependentlyOfLoading()
    {
        var state = new StatusBarState();
        var activityId = Guid.NewGuid();

        state = StatusBarReducers.ReduceSetResolverStatus(
            state,
            new StatusBarAction.SetResolverStatus("Starting resolution..."));

        Assert.Equal("Starting resolution...", state.ResolverStatus);
        Assert.Empty(state.EventsLoading);

        state = StatusBarReducers.ReduceSetEventsLoading(
            state,
            new StatusBarAction.SetEventsLoading(activityId, 100, 0));

        Assert.Equal("Starting resolution...", state.ResolverStatus);
        Assert.Single(state.EventsLoading);

        state = StatusBarReducers.ReduceSetResolverStatus(
            state,
            new StatusBarAction.SetResolverStatus("Resolution in progress..."));

        Assert.Equal("Resolution in progress...", state.ResolverStatus);
        Assert.Empty(state.EventsLoading);
    }
}
