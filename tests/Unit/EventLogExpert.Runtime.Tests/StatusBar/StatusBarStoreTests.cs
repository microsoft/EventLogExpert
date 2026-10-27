// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.StatusBar;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
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

        StatusBarAssertions.AssertResolverStatus(ResolverStatusReason.None, null, state.ResolverStatus);
    }
}

public sealed class StatusBarActionTests
{
    [Fact]
    public void ClearStatusAction_ShouldCreateAction()
    {
        var activityId = StatusActivityId.Create();
        var action = new ClearStatusAction(activityId, 0);

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
        var status = ResolverStatus.FailedToLoad("System");
        var action = new SetResolverStatusAction(status);

        StatusBarAssertions.AssertResolverStatus(ResolverStatusReason.FailedToLoad, "System", action.Status);
    }
}

public sealed class StatusBarReducerTests
{
    [Fact]
    public void ReduceClearStatus_WhenTheFinalCountExceedsTheLastReport_AddsTheFinalCountToTheEpisode()
    {
        var finishingActivity = StatusActivityId.Create();
        var state = new StatusBarState
        {
            EventsLoading = ImmutableDictionary<StatusActivityId, (int, int, long?)>.Empty
                .Add(finishingActivity, (100, 1, null))
                .Add(StatusActivityId.Create(), (50, 0, null)),
            EpisodeFailedTotal = 2
        };

        var result = Reducers.ReduceClearStatus(state, new ClearStatusAction(finishingActivity, 4));

        Assert.Equal(6, result.EpisodeFailedTotal);
        Assert.Equal(0, result.FailedEpisodeCompletionCount);
    }

    [Fact]
    public void ReduceClearStatus_WhenTheFinalCountIsBelowTheLastReport_KeepsTheReportedCount()
    {
        var finishingActivity = StatusActivityId.Create();
        var state = new StatusBarState
        {
            EventsLoading = ImmutableDictionary<StatusActivityId, (int, int, long?)>.Empty
                .Add(finishingActivity, (100, 3, null))
                .Add(StatusActivityId.Create(), (50, 0, null))
        };

        var result = Reducers.ReduceClearStatus(state, new ClearStatusAction(finishingActivity, 0));

        Assert.Equal(3, result.EpisodeFailedTotal);
    }

    [Fact]
    public void ReduceClearStatus_WhenTheLastActivityFinishesWithoutFailures_KeepsTheLastFailedCompletion()
    {
        var activityId = StatusActivityId.Create();
        var state = new StatusBarState
        {
            EventsLoading = ImmutableDictionary<StatusActivityId, (int, int, long?)>.Empty.Add(activityId, (100, 0, null)),
            FailedEpisodeCompletionCount = 7,
            LastCompletedEpisodeFailedTotal = 9
        };

        var result = Reducers.ReduceClearStatus(state, new ClearStatusAction(activityId, 0));

        Assert.Empty(result.EventsLoading);
        Assert.Equal(0, result.EpisodeFailedTotal);
        Assert.Equal(7, result.FailedEpisodeCompletionCount);
        Assert.Equal(9, result.LastCompletedEpisodeFailedTotal);
    }

    [Fact]
    public void ReduceClearStatus_WhenTheLastActivityFinishes_CompletesTheEpisodeWithItsTotal()
    {
        var activityId = StatusActivityId.Create();
        var state = new StatusBarState
        {
            EventsLoading = ImmutableDictionary<StatusActivityId, (int, int, long?)>.Empty.Add(activityId, (100, 1, null)),
            EpisodeFailedTotal = 5,
            FailedEpisodeCompletionCount = 7,
            LastCompletedEpisodeFailedTotal = 9
        };

        var result = Reducers.ReduceClearStatus(state, new ClearStatusAction(activityId, 2));

        Assert.Empty(result.EventsLoading);
        Assert.Equal(0, result.EpisodeFailedTotal);
        Assert.Equal(8, result.FailedEpisodeCompletionCount);
        Assert.Equal(7, result.LastCompletedEpisodeFailedTotal);
    }

    [Fact]
    public void ReduceClearStatus_WithEmptyState_ShouldReturnEmptyState()
    {
        var state = new StatusBarState();
        var action = new ClearStatusAction(StatusActivityId.Create(), 0);

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

        var action = new ClearStatusAction(activityId, 0);

        var result = Reducers.ReduceClearStatus(state, action);

        Assert.Empty(result.EventsLoading);
    }

    [Fact]
    public void ReduceClearStatus_WithNonExistingActivity_DoesNotCountItsFailures()
    {
        var state = new StatusBarState
        {
            EventsLoading = ImmutableDictionary<StatusActivityId, (int, int, long?)>.Empty.Add(StatusActivityId.Create(), (100, 0, null))
        };

        var result = Reducers.ReduceClearStatus(state, new ClearStatusAction(StatusActivityId.Create(), 4));

        Assert.Equal(0, result.EpisodeFailedTotal);
        Assert.Equal(0, result.FailedEpisodeCompletionCount);
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

        var action = new ClearStatusAction(nonExistingActivityId, 0);

        var result = Reducers.ReduceClearStatus(state, action);

        Assert.Single(result.EventsLoading);
        Assert.True(result.EventsLoading.ContainsKey(existingActivityId));
    }

    [Fact]
    public void ReduceCloseAll_AdvancesTheLoadGeneration()
    {
        var result = Reducers.ReduceCloseAll(new StatusBarState { LoadGeneration = 4 });

        Assert.Equal(5, result.LoadGeneration);
    }

    [Fact]
    public void ReduceCloseAll_MidEpisodeWithoutFailures_KeepsTheLastFailedCompletion()
    {
        var state = new StatusBarState
        {
            EventsLoading = ImmutableDictionary<StatusActivityId, (int, int, long?)>.Empty.Add(StatusActivityId.Create(), (100, 0, null)),
            FailedEpisodeCompletionCount = 3,
            LastCompletedEpisodeFailedTotal = 4
        };

        var result = Reducers.ReduceCloseAll(state);

        Assert.Equal(3, result.FailedEpisodeCompletionCount);
        Assert.Equal(4, result.LastCompletedEpisodeFailedTotal);
    }

    [Fact]
    public void ReduceCloseAll_MidEpisode_CompletesTheEpisodeWithTheFailuresSoFar()
    {
        var state = new StatusBarState
        {
            EventsLoading = ImmutableDictionary<StatusActivityId, (int, int, long?)>.Empty
                .Add(StatusActivityId.Create(), (100, 5, null))
                .Add(StatusActivityId.Create(), (200, 10, null)),
            EpisodeFailedTotal = 2,
            FailedEpisodeCompletionCount = 3,
            LastCompletedEpisodeFailedTotal = 1
        };

        var result = Reducers.ReduceCloseAll(state);

        Assert.Empty(result.EventsLoading);
        Assert.Equal(0, result.EpisodeFailedTotal);
        Assert.Equal(4, result.FailedEpisodeCompletionCount);
        Assert.Equal(17, result.LastCompletedEpisodeFailedTotal);
    }

    [Fact]
    public void ReduceCloseAll_ShouldReturnNewDefaultState()
    {
        var state = new StatusBarState
        {
            EventsLoading = ImmutableDictionary<StatusActivityId, (int, int, long?)>.Empty
                .Add(StatusActivityId.Create(), (100, 5, null))
                .Add(StatusActivityId.Create(), (200, 10, null)),
            ResolverStatus = ResolverStatus.FailedToLoad("System")
        };

        var result = Reducers.ReduceCloseAll(state);

        Assert.Empty(result.EventsLoading);
        StatusBarAssertions.AssertResolverStatus(ResolverStatusReason.None, null, result.ResolverStatus);
    }

    [Fact]
    public void ReduceCloseAll_WhileIdle_KeepsTheLastCompletedEpisode()
    {
        var state = new StatusBarState
        {
            FailedEpisodeCompletionCount = 3,
            LastCompletedEpisodeFailedTotal = 4,
            ResolverStatus = ResolverStatus.FailedToLoad("System")
        };

        var result = Reducers.ReduceCloseAll(state);

        Assert.Equal(3, result.FailedEpisodeCompletionCount);
        Assert.Equal(4, result.LastCompletedEpisodeFailedTotal);
        StatusBarAssertions.AssertResolverStatus(ResolverStatusReason.None, null, result.ResolverStatus);
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

        var state = new StatusBarState
        {
            EventsLoading = ImmutableDictionary<StatusActivityId, (int, int, long?)>.Empty
                .Add(activityId1, (100, 5, null))
                .Add(activityId2, (200, 10, null))
        };

        var result = Reducers.ReduceSetEventsLoading(state, new SetEventsLoadingAction(activityId2, 300, 15));

        Assert.Equal(2, result.EventsLoading.Count);
        Assert.Equal((100, 5, (long?)null), result.EventsLoading[activityId1]);
        Assert.Equal((300, 15, (long?)null), result.EventsLoading[activityId2]);
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
    public void ReduceSetEventsLoading_WithUnknownActivity_IgnoresItAndReturnsSameState()
    {
        var state = new StatusBarState
        {
            EventsLoading = ImmutableDictionary<StatusActivityId, (int, int, long?)>.Empty.Add(StatusActivityId.Create(), (100, 5, null))
        };

        var result = Reducers.ReduceSetEventsLoading(state, new SetEventsLoadingAction(StatusActivityId.Create(), 100, 5));

        Assert.Same(state, result);
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
        var state = new StatusBarState { ResolverStatus = ResolverStatus.FailedToLoad("Old") };
        var newStatus = ResolverStatus.FailedToLoad("New");
        var action = new SetResolverStatusAction(newStatus);

        var result = Reducers.ReduceSetResolverStatus(state, action);

        StatusBarAssertions.AssertResolverStatus(ResolverStatusReason.FailedToLoad, "New", result.ResolverStatus);
    }

    [Fact]
    public void ReduceSetResolverStatus_ShouldSetStatus()
    {
        var state = new StatusBarState();
        var status = ResolverStatus.NoResolver;
        var action = new SetResolverStatusAction(status);

        var result = Reducers.ReduceSetResolverStatus(state, action);

        StatusBarAssertions.AssertResolverStatus(ResolverStatusReason.NoResolver, null, result.ResolverStatus);
    }

    [Fact]
    public void ReduceSetResolverStatus_WithNone_ShouldClearStatus()
    {
        var state = new StatusBarState { ResolverStatus = ResolverStatus.FailedToLoad("System") };
        var action = new SetResolverStatusAction(ResolverStatus.None);

        var result = Reducers.ReduceSetResolverStatus(state, action);

        StatusBarAssertions.AssertResolverStatus(ResolverStatusReason.None, null, result.ResolverStatus);
    }

    [Fact]
    public void ReduceSetResolverStatus_WithSameStatus_ShouldReturnSameState()
    {
        var status = ResolverStatus.FailedToLoad("System");
        var state = new StatusBarState { ResolverStatus = status };
        var action = new SetResolverStatusAction(status);

        var result = Reducers.ReduceSetResolverStatus(state, action);

        Assert.Same(state, result);
    }

    [Fact]
    public void ReduceStartEventsLoading_WithCurrentGeneration_RegistersActivity()
    {
        var activityId = StatusActivityId.Create();

        var result = Reducers.ReduceStartEventsLoading(
            new StatusBarState { LoadGeneration = 2 },
            new StartEventsLoadingAction(activityId, 2));

        Assert.True(result.EventsLoading.ContainsKey(activityId));
    }

    [Fact]
    public void ReduceStartEventsLoading_WithExistingActivity_KeepsItsProgress()
    {
        var activityId = StatusActivityId.Create();

        var state = new StatusBarState
        {
            EventsLoading = ImmutableDictionary<StatusActivityId, (int, int, long?)>.Empty.Add(activityId, (100, 5, 1_000))
        };

        var result = Reducers.ReduceStartEventsLoading(state, new StartEventsLoadingAction(activityId, 0));

        Assert.Same(state, result);
    }

    [Fact]
    public void ReduceStartEventsLoading_WithNewActivity_RegistersPendingActivity()
    {
        var activityId = StatusActivityId.Create();

        var result = Reducers.ReduceStartEventsLoading(new StatusBarState(), new StartEventsLoadingAction(activityId, 0));

        Assert.Single(result.EventsLoading);
        Assert.Equal((0, 0, (long?)null), result.EventsLoading[activityId]);
    }

    [Fact]
    public void ReduceStartEventsLoading_WithStaleGeneration_IsIgnored()
    {
        var state = new StatusBarState { LoadGeneration = 2 };

        var result = Reducers.ReduceStartEventsLoading(state, new StartEventsLoadingAction(StatusActivityId.Create(), 1));

        Assert.Same(state, result);
    }
}

public sealed class StatusBarIntegrationTests
{
    [Fact]
    public void ActivityLifecycle_ShouldHandleAddUpdateRemove()
    {
        var state = new StatusBarState();
        var activityId = StatusActivityId.Create();

        state = Reducers.ReduceStartEventsLoading(state, new StartEventsLoadingAction(activityId, 0));
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
            new ClearStatusAction(activityId, 0));

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

        state = Reducers.ReduceClearStatus(state, new ClearStatusAction(nonExisting, 0));

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
            ResolverStatus = ResolverStatus.FailedToLoad("System")
        };

        state = Reducers.ReduceCloseAll(state);

        Assert.Empty(state.EventsLoading);
        StatusBarAssertions.AssertResolverStatus(ResolverStatusReason.None, null, state.ResolverStatus);
    }

    [Fact]
    public void CompleteWorkflow_ShouldHandleLoadingAndResolution()
    {
        var state = new StatusBarState();
        var activityId = StatusActivityId.Create();

        state = Reducers.ReduceStartEventsLoading(state, new StartEventsLoadingAction(activityId, 0));
        state = Reducers.ReduceSetEventsLoading(
            state,
            new SetEventsLoadingAction(activityId, 100, 0));

        Assert.Single(state.EventsLoading);

        state = Reducers.ReduceSetResolverStatus(
            state,
            new SetResolverStatusAction(ResolverStatus.FailedToLoad("System")));

        StatusBarAssertions.AssertResolverStatus(ResolverStatusReason.FailedToLoad, "System", state.ResolverStatus);
        Assert.Single(state.EventsLoading);

        state = Reducers.ReduceClearStatus(
            state,
            new ClearStatusAction(activityId, 0));

        Assert.Empty(state.EventsLoading);
        StatusBarAssertions.AssertResolverStatus(ResolverStatusReason.FailedToLoad, "System", state.ResolverStatus);
    }

    [Fact]
    public void ConcurrentActivities_FinishingAtDifferentTimes_CompleteTheEpisodeWithTheSumOfTheirFailures()
    {
        var firstActivity = StatusActivityId.Create();
        var secondActivity = StatusActivityId.Create();
        var state = new StatusBarState();

        state = Reducers.ReduceStartEventsLoading(state, new StartEventsLoadingAction(firstActivity, 0));
        state = Reducers.ReduceStartEventsLoading(state, new StartEventsLoadingAction(secondActivity, 0));
        state = Reducers.ReduceSetEventsLoading(state, new SetEventsLoadingAction(firstActivity, 100, 2));
        state = Reducers.ReduceClearStatus(state, new ClearStatusAction(firstActivity, 3));
        state = Reducers.ReduceSetEventsLoading(state, new SetEventsLoadingAction(secondActivity, 200, 4));

        Assert.Equal(3, state.EpisodeFailedTotal);
        Assert.Equal(0, state.FailedEpisodeCompletionCount);

        state = Reducers.ReduceClearStatus(state, new ClearStatusAction(secondActivity, 5));

        Assert.Equal(1, state.FailedEpisodeCompletionCount);
        Assert.Equal(8, state.LastCompletedEpisodeFailedTotal);
        Assert.Equal(0, state.EpisodeFailedTotal);
    }

    [Fact]
    public void LoadingProgress_ShouldTrackMultipleActivities()
    {
        var state = new StatusBarState();
        var activity1 = StatusActivityId.Create();
        var activity2 = StatusActivityId.Create();

        state = Reducers.ReduceStartEventsLoading(state, new StartEventsLoadingAction(activity1, 0));

        state = Reducers.ReduceSetEventsLoading(
            state,
            new SetEventsLoadingAction(activity1, 100, 0));

        Assert.Single(state.EventsLoading);

        state = Reducers.ReduceStartEventsLoading(state, new StartEventsLoadingAction(activity2, 0));

        state = Reducers.ReduceSetEventsLoading(
            state,
            new SetEventsLoadingAction(activity2, 200, 5));

        Assert.Equal(2, state.EventsLoading.Count);

        state = Reducers.ReduceSetEventsLoading(
            state,
            new SetEventsLoadingAction(activity1, 50, 0));

        Assert.Equal(2, state.EventsLoading.Count);
        Assert.Equal((50, 0, (long?)null), state.EventsLoading[activity1]);

        state = Reducers.ReduceClearStatus(state, new ClearStatusAction(activity1, 0));
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

        state = Reducers.ReduceStartEventsLoading(state, new StartEventsLoadingAction(activity1, 0));
        state = Reducers.ReduceStartEventsLoading(state, new StartEventsLoadingAction(activity2, 0));
        state = Reducers.ReduceStartEventsLoading(state, new StartEventsLoadingAction(activity3, 0));

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
    public void NewEpisode_AfterACompletion_StartsItsFailureTotalAtZero()
    {
        var firstActivity = StatusActivityId.Create();
        var secondActivity = StatusActivityId.Create();
        var state = new StatusBarState();

        state = Reducers.ReduceStartEventsLoading(state, new StartEventsLoadingAction(firstActivity, 0));
        state = Reducers.ReduceSetEventsLoading(state, new SetEventsLoadingAction(firstActivity, 0, 3));
        state = Reducers.ReduceClearStatus(state, new ClearStatusAction(firstActivity, 3));
        state = Reducers.ReduceStartEventsLoading(state, new StartEventsLoadingAction(secondActivity, 0));

        Assert.Equal(0, state.EpisodeFailedTotal);
        Assert.Equal(1, state.FailedEpisodeCompletionCount);
        Assert.Equal(3, state.LastCompletedEpisodeFailedTotal);

        state = Reducers.ReduceClearStatus(state, new ClearStatusAction(secondActivity, 1));

        Assert.Equal(2, state.FailedEpisodeCompletionCount);
        Assert.Equal(1, state.LastCompletedEpisodeFailedTotal);
    }

    [Fact]
    public void ReduceClearStatus_WithConcurrentLoads_RemovesOnlyTheClearedActivity()
    {
        var firstActivity = StatusActivityId.Create();
        var secondActivity = StatusActivityId.Create();

        var state = new StatusBarState();
        state = Reducers.ReduceStartEventsLoading(state, new StartEventsLoadingAction(firstActivity, 0));
        state = Reducers.ReduceStartEventsLoading(state, new StartEventsLoadingAction(secondActivity, 0));
        state = Reducers.ReduceSetEventsLoading(state, new SetEventsLoadingAction(firstActivity, 100, 0));
        state = Reducers.ReduceSetEventsLoading(state, new SetEventsLoadingAction(secondActivity, 50, 0));

        Assert.Equal(2, state.EventsLoading.Count);

        state = Reducers.ReduceClearStatus(state, new ClearStatusAction(firstActivity, 0));

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
            new SetResolverStatusAction(ResolverStatus.FailedToLoad("Starting resolution")));

        StatusBarAssertions.AssertResolverStatus(ResolverStatusReason.FailedToLoad, "Starting resolution", state.ResolverStatus);
        Assert.Empty(state.EventsLoading);

        state = Reducers.ReduceStartEventsLoading(state, new StartEventsLoadingAction(activityId, 0));

        StatusBarAssertions.AssertResolverStatus(ResolverStatusReason.FailedToLoad, "Starting resolution", state.ResolverStatus);
        Assert.Single(state.EventsLoading);

        state = Reducers.ReduceSetResolverStatus(
            state,
            new SetResolverStatusAction(ResolverStatus.FailedToLoad("Resolution in progress")));

        StatusBarAssertions.AssertResolverStatus(ResolverStatusReason.FailedToLoad, "Resolution in progress", state.ResolverStatus);
        Assert.Single(state.EventsLoading);
    }
}

public sealed class StatusBarThrottledStoreTests
{
    [Fact]
    public async Task ThrottledNotifications_ReadTheCompletedEpisodesFromTheLatestState()
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddFluxor(options => options.ScanTypes(typeof(StatusBarState), typeof(Reducers)))
            .BuildServiceProvider();

        await provider.GetRequiredService<IStore>().InitializeAsync();
        var dispatcher = provider.GetRequiredService<IDispatcher>();
        var state = provider.GetRequiredService<IState<StatusBarState>>();
        var cancellationToken = TestContext.Current.CancellationToken;
        var observedStates = new List<StatusBarState>();
        TaskCompletionSource firstEpisodeObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource secondEpisodeObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        state.StateChanged += (_, _) =>
        {
            var observed = state.Value;

            lock (observedStates) { observedStates.Add(observed); }

            if (observed.FailedEpisodeCompletionCount >= 1) { firstEpisodeObserved.TrySetResult(); }

            if (observed.FailedEpisodeCompletionCount >= 2) { secondEpisodeObserved.TrySetResult(); }
        };

        var firstActivity = StatusActivityId.Create();
        dispatcher.Dispatch(new StartEventsLoadingAction(firstActivity, 0));
        dispatcher.Dispatch(new SetEventsLoadingAction(firstActivity, 100, 1));
        dispatcher.Dispatch(new ClearStatusAction(firstActivity, 3));
        await firstEpisodeObserved.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

        var completedEpisodeEnd = StatusActivityId.Create();
        var nextEpisodeStart = StatusActivityId.Create();
        dispatcher.Dispatch(new StartEventsLoadingAction(completedEpisodeEnd, 0));
        dispatcher.Dispatch(new SetEventsLoadingAction(completedEpisodeEnd, 0, 2));
        dispatcher.Dispatch(new ClearStatusAction(completedEpisodeEnd, 2));
        dispatcher.Dispatch(new StartEventsLoadingAction(nextEpisodeStart, 0));
        await secondEpisodeObserved.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

        List<StatusBarState> observedSnapshot;
        lock (observedStates) { observedSnapshot = [.. observedStates]; }

        Assert.All(
            observedSnapshot.Where(observed => observed.FailedEpisodeCompletionCount == 1),
            observed => Assert.Equal(3, observed.LastCompletedEpisodeFailedTotal));
        var lastObserved = observedSnapshot[^1];
        Assert.Equal(2, lastObserved.FailedEpisodeCompletionCount);
        Assert.Equal(2, lastObserved.LastCompletedEpisodeFailedTotal);
        Assert.Equal(0, lastObserved.EpisodeFailedTotal);
        Assert.True(lastObserved.EventsLoading.ContainsKey(nextEpisodeStart));
    }
}

public sealed class StatusBarCloseAllRaceTests
{
    [Fact]
    public async Task CloseAll_ThenFailureFreeCompletion_KeepsTheFailedCompletion()
    {
        using ServiceProvider provider = await CreateStoreAsync();
        var dispatcher = provider.GetRequiredService<IDispatcher>();
        var state = provider.GetRequiredService<IState<StatusBarState>>();
        var activity = StatusActivityId.Create();

        dispatcher.Dispatch(new StartEventsLoadingAction(activity, 0));
        dispatcher.Dispatch(new SetEventsLoadingAction(activity, 10, 3));
        dispatcher.Dispatch(new CloseAllLogsAction());
        dispatcher.Dispatch(new ClearStatusAction(activity, 0));

        AssertSingleEpisode(state.Value, expectedFailedTotal: 3);
    }

    [Fact]
    public async Task CloseAll_ThenStaleStart_IsIgnoredAndCreatesNoEpisode()
    {
        using ServiceProvider provider = await CreateStoreAsync();
        var dispatcher = provider.GetRequiredService<IDispatcher>();
        var state = provider.GetRequiredService<IState<StatusBarState>>();
        var failingActivity = StatusActivityId.Create();
        var cancelledActivity = StatusActivityId.Create();

        dispatcher.Dispatch(new StartEventsLoadingAction(failingActivity, 0));
        dispatcher.Dispatch(new SetEventsLoadingAction(failingActivity, 10, 3));
        dispatcher.Dispatch(new CloseAllLogsAction());
        dispatcher.Dispatch(new StartEventsLoadingAction(cancelledActivity, 0));
        dispatcher.Dispatch(new ClearStatusAction(cancelledActivity, 0));

        Assert.Empty(state.Value.EventsLoading);
        AssertSingleEpisode(state.Value, expectedFailedTotal: 3);

        RunEpisodeWithTwoFailures(dispatcher, state);
    }

    [Fact]
    public async Task CloseAll_WithProgressTickAfterIt_DoesNotResurrectTheClosedActivity()
    {
        using ServiceProvider provider = await CreateStoreAsync();
        var dispatcher = provider.GetRequiredService<IDispatcher>();
        var state = provider.GetRequiredService<IState<StatusBarState>>();
        var activity = StatusActivityId.Create();

        dispatcher.Dispatch(new StartEventsLoadingAction(activity, 0));
        dispatcher.Dispatch(new SetEventsLoadingAction(activity, 10, 3));
        dispatcher.Dispatch(new CloseAllLogsAction());
        dispatcher.Dispatch(new SetEventsLoadingAction(activity, 12, 3));

        Assert.Empty(state.Value.EventsLoading);

        dispatcher.Dispatch(new ClearStatusAction(activity, 3));

        Assert.Empty(state.Value.EventsLoading);
        AssertSingleEpisode(state.Value, expectedFailedTotal: 3);

        RunEpisodeWithTwoFailures(dispatcher, state);
    }

    [Fact]
    public async Task CloseAll_WithProgressTickBeforeIt_CompletesTheEpisodeOnceAndStartsTheNextAtZero()
    {
        using ServiceProvider provider = await CreateStoreAsync();
        var dispatcher = provider.GetRequiredService<IDispatcher>();
        var state = provider.GetRequiredService<IState<StatusBarState>>();
        var activity = StatusActivityId.Create();

        dispatcher.Dispatch(new StartEventsLoadingAction(activity, 0));
        dispatcher.Dispatch(new SetEventsLoadingAction(activity, 10, 3));
        dispatcher.Dispatch(new CloseAllLogsAction());
        dispatcher.Dispatch(new ClearStatusAction(activity, 3));

        AssertSingleEpisode(state.Value, expectedFailedTotal: 3);

        RunEpisodeWithTwoFailures(dispatcher, state);
    }

    [Fact]
    public async Task FailedEpisode_ThenFailureFreeEpisode_ThenFailedEpisode_CountsOnlyTheFailedOnes()
    {
        using ServiceProvider provider = await CreateStoreAsync();
        var dispatcher = provider.GetRequiredService<IDispatcher>();
        var state = provider.GetRequiredService<IState<StatusBarState>>();
        var failing = StatusActivityId.Create();
        var failureFree = StatusActivityId.Create();

        dispatcher.Dispatch(new StartEventsLoadingAction(failing, 0));
        dispatcher.Dispatch(new ClearStatusAction(failing, 3));
        dispatcher.Dispatch(new StartEventsLoadingAction(failureFree, 0));
        dispatcher.Dispatch(new ClearStatusAction(failureFree, 0));

        AssertSingleEpisode(state.Value, expectedFailedTotal: 3);

        RunEpisodeWithTwoFailures(dispatcher, state);
    }

    private static void AssertSingleEpisode(StatusBarState state, int expectedFailedTotal)
    {
        Assert.Equal(1, state.FailedEpisodeCompletionCount);
        Assert.Equal(expectedFailedTotal, state.LastCompletedEpisodeFailedTotal);
        Assert.Equal(0, state.EpisodeFailedTotal);
    }

    private static async Task<ServiceProvider> CreateStoreAsync()
    {
        ServiceProvider provider = new ServiceCollection()
            .AddFluxor(options => options.ScanTypes(typeof(StatusBarState), typeof(Reducers)))
            .BuildServiceProvider();

        await provider.GetRequiredService<IStore>().InitializeAsync();

        return provider;
    }

    private static void RunEpisodeWithTwoFailures(IDispatcher dispatcher, IState<StatusBarState> state)
    {
        var nextActivity = StatusActivityId.Create();

        dispatcher.Dispatch(new StartEventsLoadingAction(nextActivity, state.Value.LoadGeneration));
        dispatcher.Dispatch(new SetEventsLoadingAction(nextActivity, 5, 2));
        dispatcher.Dispatch(new ClearStatusAction(nextActivity, 2));

        Assert.Equal(2, state.Value.FailedEpisodeCompletionCount);
        Assert.Equal(2, state.Value.LastCompletedEpisodeFailedTotal);
    }
}

internal static class StatusBarAssertions
{
    internal static void AssertResolverStatus(ResolverStatusReason expectedReason, string? expectedLogDescription, ResolverStatus actual)
    {
        Assert.Equal(expectedReason, actual.Reason);
        Assert.Equal(expectedLogDescription, actual.LogDescription);
    }
}
