// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.StatusBar;
using System.Collections.Immutable;
using CloseAllLogsAction = EventLogExpert.Runtime.EventLog.CloseAllLogsAction;

namespace EventLogExpert.Runtime.Tests.StatusBar;

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
        var activityId = StatusActivityId.Create();
        var action = new ClearStatusAction(activityId);

        Assert.Equal(activityId, action.ActivityId);
    }

    [Fact]
    public void CloseAllLogsAction_ShouldCreateAction()
    {
        var action = new CloseAllLogsAction();

        Assert.NotNull(action);
    }

    [Fact]
    public void SetEventsLoadingAction_ShouldCreateAction()
    {
        var activityId = StatusActivityId.Create();
        var count = 100;
        var failedCount = 5;
        var action = new SetEventsLoadingAction(activityId, count, failedCount);

        Assert.Equal(activityId, action.ActivityId);
        Assert.Equal(count, action.Count);
        Assert.Equal(failedCount, action.FailedCount);
    }

    [Fact]
    public void SetResolverStatusAction_ShouldCreateAction()
    {
        var status = "Resolving events...";
        var action = new SetResolverStatusAction(status);

        Assert.Equal(status, action.ResolverStatus);
    }
}

public sealed class StatusBarReducerTests
{
    [Fact]
    public void ReduceClearStatus_WithEmptyState_ShouldReturnEmptyState()
    {
        var state = new StatusBarState();
        var action = new ClearStatusAction(StatusActivityId.Create());

        var result = Reducers.ReduceClearStatus(state, action);

        Assert.Empty(result.EventsLoading);
    }

    [Fact]
    public void ReduceClearStatus_WithExistingActivity_ShouldRemoveActivity()
    {
        var activityId = StatusActivityId.Create();

        var state = new StatusBarState
        {
            EventsLoading = ImmutableDictionary<StatusActivityId, (int, int, long?)>.Empty.Add(activityId, (100, 5, null))
        };

        var action = new ClearStatusAction(activityId);

        var result = Reducers.ReduceClearStatus(state, action);

        Assert.Empty(result.EventsLoading);
    }

    [Fact]
    public void ReduceClearStatus_WithNonExistingActivity_ShouldReturnStateWithoutChange()
    {
        var existingActivityId = StatusActivityId.Create();
        var nonExistingActivityId = StatusActivityId.Create();

        var state = new StatusBarState
        {
            EventsLoading = ImmutableDictionary<StatusActivityId, (int, int, long?)>.Empty.Add(existingActivityId, (100, 5, null))
        };

        var action = new ClearStatusAction(nonExistingActivityId);

        var result = Reducers.ReduceClearStatus(state, action);

        Assert.Single(result.EventsLoading);
        Assert.True(result.EventsLoading.ContainsKey(existingActivityId));
    }

    [Fact]
    public void ReduceCloseAll_ShouldReturnNewDefaultState()
    {
        var state = new StatusBarState
        {
            EventsLoading = ImmutableDictionary<StatusActivityId, (int, int, long?)>.Empty
                .Add(StatusActivityId.Create(), (100, 5, null))
                .Add(StatusActivityId.Create(), (200, 10, null)),
            ResolverStatus = "Processing..."
        };

        var result = Reducers.ReduceCloseAll(state);

        Assert.Empty(result.EventsLoading);
        Assert.Equal(string.Empty, result.ResolverStatus);
    }

    [Fact]
    public void ReduceSetEventsLoading_AfterTotalSet_PreservesTotal()
    {
        var activityId = StatusActivityId.Create();

        var state = new StatusBarState
        {
            EventsLoading = ImmutableDictionary<StatusActivityId, (int, int, long?)>.Empty.Add(activityId, (50, 2, 10_000))
        };

        var result = Reducers.ReduceSetEventsLoading(state, new SetEventsLoadingAction(activityId, 100, 5));

        Assert.Equal((100, 5, (long?)10_000), result.EventsLoading[activityId]);
    }

    [Fact]
    public void ReduceSetEventsLoading_WithExistingActivity_ShouldUpdateActivity()
    {
        var activityId = StatusActivityId.Create();

        var state = new StatusBarState
        {
            EventsLoading = ImmutableDictionary<StatusActivityId, (int, int, long?)>.Empty.Add(activityId, (50, 2, null))
        };

        var action = new SetEventsLoadingAction(activityId, 100, 5);

        var result = Reducers.ReduceSetEventsLoading(state, action);

        Assert.Single(result.EventsLoading);
        Assert.Equal((100, 5, (long?)null), result.EventsLoading[activityId]);
    }

    [Fact]
    public void ReduceSetEventsLoading_WithMultipleActivities_ShouldMaintainOthers()
    {
        var activityId1 = StatusActivityId.Create();
        var activityId2 = StatusActivityId.Create();
        var activityId3 = StatusActivityId.Create();

        var state = new StatusBarState
        {
            EventsLoading = ImmutableDictionary<StatusActivityId, (int, int, long?)>.Empty
                .Add(activityId1, (100, 5, null))
                .Add(activityId2, (200, 10, null))
        };

        var action = new SetEventsLoadingAction(activityId3, 300, 15);

        var result = Reducers.ReduceSetEventsLoading(state, action);

        Assert.Equal(3, result.EventsLoading.Count);
        Assert.True(result.EventsLoading.ContainsKey(activityId1));
        Assert.True(result.EventsLoading.ContainsKey(activityId2));
        Assert.True(result.EventsLoading.ContainsKey(activityId3));
    }

    [Fact]
    public void ReduceSetEventsLoading_WithNewActivity_ShouldAddActivity()
    {
        var state = new StatusBarState();
        var activityId = StatusActivityId.Create();
        var action = new SetEventsLoadingAction(activityId, 100, 5);

        var result = Reducers.ReduceSetEventsLoading(state, action);

        Assert.Single(result.EventsLoading);
        Assert.True(result.EventsLoading.ContainsKey(activityId));
        Assert.Equal((100, 5, (long?)null), result.EventsLoading[activityId]);
    }

    [Fact]
    public void ReduceSetEventsLoading_WithUnchangedValues_ShouldReturnSameState()
    {
        var activityId = StatusActivityId.Create();

        var state = new StatusBarState
        {
            EventsLoading = ImmutableDictionary<StatusActivityId, (int, int, long?)>.Empty.Add(activityId, (100, 5, null))
        };

        var action = new SetEventsLoadingAction(activityId, 100, 5);

        var result = Reducers.ReduceSetEventsLoading(state, action);

        Assert.Same(state, result);
    }

    [Fact]
    public void ReduceSetEventsLoading_WithZeroCount_ShouldRegisterPendingActivity()
    {
        var activityId = StatusActivityId.Create();

        var state = new StatusBarState();

        var action = new SetEventsLoadingAction(activityId, 0, 0);

        var result = Reducers.ReduceSetEventsLoading(state, action);

        Assert.Single(result.EventsLoading);
        Assert.Equal((0, 0, (long?)null), result.EventsLoading[activityId]);
    }

    [Fact]
    public void ReduceSetLoadingTotal_WithAbsentActivity_ReturnsSameState()
    {
        var state = new StatusBarState
        {
            EventsLoading = ImmutableDictionary<StatusActivityId, (int, int, long?)>.Empty.Add(StatusActivityId.Create(), (50, 2, null))
        };

        var result = Reducers.ReduceSetLoadingTotal(state, new SetLoadingTotalAction(StatusActivityId.Create(), 10_000));

        Assert.Same(state, result);
    }

    [Fact]
    public void ReduceSetLoadingTotal_WithEqualTotal_ReturnsSameState()
    {
        var activityId = StatusActivityId.Create();

        var state = new StatusBarState
        {
            EventsLoading = ImmutableDictionary<StatusActivityId, (int, int, long?)>.Empty.Add(activityId, (50, 2, 10_000))
        };

        var result = Reducers.ReduceSetLoadingTotal(state, new SetLoadingTotalAction(activityId, 10_000));

        Assert.Same(state, result);
    }

    [Fact]
    public void ReduceSetLoadingTotal_WithPresentActivity_SetsTotalPreservingCounts()
    {
        var activityId = StatusActivityId.Create();

        var state = new StatusBarState
        {
            EventsLoading = ImmutableDictionary<StatusActivityId, (int, int, long?)>.Empty.Add(activityId, (50, 2, null))
        };

        var result = Reducers.ReduceSetLoadingTotal(state, new SetLoadingTotalAction(activityId, 10_000));

        Assert.Single(result.EventsLoading);
        Assert.Equal((50, 2, (long?)10_000), result.EventsLoading[activityId]);
    }

    [Fact]
    public void ReduceSetResolverStatus_ShouldReplaceExistingStatus()
    {
        var state = new StatusBarState { ResolverStatus = "Old status" };
        var newStatus = "New status";
        var action = new SetResolverStatusAction(newStatus);

        var result = Reducers.ReduceSetResolverStatus(state, action);

        Assert.Equal(newStatus, result.ResolverStatus);
    }

    [Fact]
    public void ReduceSetResolverStatus_ShouldSetStatus()
    {
        var state = new StatusBarState();
        var status = "Resolving 50 events...";
        var action = new SetResolverStatusAction(status);

        var result = Reducers.ReduceSetResolverStatus(state, action);

        Assert.Equal(status, result.ResolverStatus);
    }

    [Fact]
    public void ReduceSetResolverStatus_WithEmptyString_ShouldClearStatus()
    {
        var state = new StatusBarState { ResolverStatus = "Processing..." };
        var action = new SetResolverStatusAction(string.Empty);

        var result = Reducers.ReduceSetResolverStatus(state, action);

        Assert.Equal(string.Empty, result.ResolverStatus);
    }
}

public sealed class StatusBarIntegrationTests
{
    [Fact]
    public void ActivityLifecycle_ShouldHandleAddUpdateRemove()
    {
        var state = new StatusBarState();
        var activityId = StatusActivityId.Create();

        state = Reducers.ReduceSetEventsLoading(
            state,
            new SetEventsLoadingAction(activityId, 100, 0));

        Assert.Single(state.EventsLoading);
        Assert.Equal((100, 0, (long?)null), state.EventsLoading[activityId]);

        state = Reducers.ReduceSetEventsLoading(
            state,
            new SetEventsLoadingAction(activityId, 75, 2));

        Assert.Single(state.EventsLoading);
        Assert.Equal((75, 2, (long?)null), state.EventsLoading[activityId]);

        state = Reducers.ReduceSetEventsLoading(
            state,
            new SetEventsLoadingAction(activityId, 50, 5));

        Assert.Single(state.EventsLoading);
        Assert.Equal((50, 5, (long?)null), state.EventsLoading[activityId]);

        state = Reducers.ReduceClearStatus(
            state,
            new ClearStatusAction(activityId));

        Assert.Empty(state.EventsLoading);
    }

    [Fact]
    public void ClearNonExistingActivities_ShouldNotAffectOthers()
    {
        var activity1 = StatusActivityId.Create();
        var activity2 = StatusActivityId.Create();
        var nonExisting = StatusActivityId.Create();

        var state = new StatusBarState
        {
            EventsLoading = ImmutableDictionary<StatusActivityId, (int, int, long?)>.Empty
                .Add(activity1, (100, 5, null))
                .Add(activity2, (200, 10, null))
        };

        state = Reducers.ReduceClearStatus(state, new ClearStatusAction(nonExisting));

        Assert.Equal(2, state.EventsLoading.Count);
        Assert.True(state.EventsLoading.ContainsKey(activity1));
        Assert.True(state.EventsLoading.ContainsKey(activity2));
    }

    [Fact]
    public void CloseAll_ShouldResetAllState()
    {
        var state = new StatusBarState
        {
            EventsLoading = ImmutableDictionary<StatusActivityId, (int, int, long?)>.Empty
                .Add(StatusActivityId.Create(), (100, 5, null))
                .Add(StatusActivityId.Create(), (200, 10, null))
                .Add(StatusActivityId.Create(), (300, 15, null)),
            ResolverStatus = "Processing multiple activities..."
        };

        state = Reducers.ReduceCloseAll(state);

        Assert.Empty(state.EventsLoading);
        Assert.Equal(string.Empty, state.ResolverStatus);
    }

    [Fact]
    public void CompleteWorkflow_ShouldHandleLoadingAndResolution()
    {
        var state = new StatusBarState();
        var activityId = StatusActivityId.Create();

        state = Reducers.ReduceSetEventsLoading(
            state,
            new SetEventsLoadingAction(activityId, 100, 0));

        Assert.Single(state.EventsLoading);

        state = Reducers.ReduceSetResolverStatus(
            state,
            new SetResolverStatusAction("Resolving events..."));

        Assert.Equal("Resolving events...", state.ResolverStatus);
        Assert.Single(state.EventsLoading);

        state = Reducers.ReduceClearStatus(
            state,
            new ClearStatusAction(activityId));

        Assert.Empty(state.EventsLoading);
        Assert.Equal("Resolving events...", state.ResolverStatus);
    }

    [Fact]
    public void LoadingProgress_ShouldTrackMultipleActivities()
    {
        var state = new StatusBarState();
        var activity1 = StatusActivityId.Create();
        var activity2 = StatusActivityId.Create();

        state = Reducers.ReduceSetEventsLoading(
            state,
            new SetEventsLoadingAction(activity1, 100, 0));

        Assert.Single(state.EventsLoading);

        state = Reducers.ReduceSetEventsLoading(
            state,
            new SetEventsLoadingAction(activity2, 200, 5));

        Assert.Equal(2, state.EventsLoading.Count);

        state = Reducers.ReduceSetEventsLoading(
            state,
            new SetEventsLoadingAction(activity1, 50, 0));

        Assert.Equal(2, state.EventsLoading.Count);
        Assert.Equal((50, 0, (long?)null), state.EventsLoading[activity1]);

        state = Reducers.ReduceClearStatus(state, new ClearStatusAction(activity1));
        Assert.Single(state.EventsLoading);
        Assert.True(state.EventsLoading.ContainsKey(activity2));
    }

    [Fact]
    public void MultipleActivitiesWithFailures_ShouldTrackIndependently()
    {
        var state = new StatusBarState();
        var activity1 = StatusActivityId.Create();
        var activity2 = StatusActivityId.Create();
        var activity3 = StatusActivityId.Create();

        state = Reducers.ReduceSetEventsLoading(
            state,
            new SetEventsLoadingAction(activity1, 100, 0));

        state = Reducers.ReduceSetEventsLoading(
            state,
            new SetEventsLoadingAction(activity2, 200, 10));

        state = Reducers.ReduceSetEventsLoading(
            state,
            new SetEventsLoadingAction(activity3, 150, 5));

        Assert.Equal(3, state.EventsLoading.Count);
        Assert.Equal((100, 0, (long?)null), state.EventsLoading[activity1]);
        Assert.Equal((200, 10, (long?)null), state.EventsLoading[activity2]);
        Assert.Equal((150, 5, (long?)null), state.EventsLoading[activity3]);
    }

    [Fact]
    public void ReduceClearStatus_WithConcurrentLoads_RemovesOnlyTheClearedActivity()
    {
        var firstActivity = StatusActivityId.Create();
        var secondActivity = StatusActivityId.Create();

        var state = new StatusBarState();
        state = Reducers.ReduceSetEventsLoading(state, new SetEventsLoadingAction(firstActivity, 100, 0));
        state = Reducers.ReduceSetEventsLoading(state, new SetEventsLoadingAction(secondActivity, 50, 0));

        Assert.Equal(2, state.EventsLoading.Count);

        state = Reducers.ReduceClearStatus(state, new ClearStatusAction(firstActivity));

        Assert.Single(state.EventsLoading);
        Assert.Equal((50, 0, (long?)null), state.EventsLoading[secondActivity]);
        Assert.False(state.EventsLoading.ContainsKey(firstActivity));
    }

    [Fact]
    public void ResolverStatus_ShouldUpdateIndependentlyOfLoading()
    {
        var state = new StatusBarState();
        var activityId = StatusActivityId.Create();

        state = Reducers.ReduceSetResolverStatus(
            state,
            new SetResolverStatusAction("Starting resolution..."));

        Assert.Equal("Starting resolution...", state.ResolverStatus);
        Assert.Empty(state.EventsLoading);

        state = Reducers.ReduceSetEventsLoading(
            state,
            new SetEventsLoadingAction(activityId, 100, 0));

        Assert.Equal("Starting resolution...", state.ResolverStatus);
        Assert.Single(state.EventsLoading);

        state = Reducers.ReduceSetResolverStatus(
            state,
            new SetResolverStatusAction("Resolution in progress..."));

        Assert.Equal("Resolution in progress...", state.ResolverStatus);
        Assert.Single(state.EventsLoading);
    }
}
