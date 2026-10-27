// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.EventLog;
using Fluxor;

namespace EventLogExpert.Runtime.StatusBar;

public sealed class Reducers
{
    [ReducerMethod]
    public static StatusBarState ReduceClearStatus(StatusBarState state, ClearStatusAction action)
    {
        if (!state.EventsLoading.TryGetValue(action.ActivityId, out var existing))
        {
            return state with { };
        }

        var remainingLoading = state.EventsLoading.Remove(action.ActivityId);
        int episodeFailedTotal = state.EpisodeFailedTotal + Math.Max(action.FinalFailedCount, existing.Failed);

        if (!remainingLoading.IsEmpty)
        {
            return state with { EventsLoading = remainingLoading, EpisodeFailedTotal = episodeFailedTotal };
        }

        // Only a completion with failures is announced, so a zero-failure completion must not displace its total.
        return episodeFailedTotal > 0 ?
            state with
            {
                EventsLoading = remainingLoading,
                EpisodeFailedTotal = 0,
                FailedEpisodeCompletionCount = state.FailedEpisodeCompletionCount + 1,
                LastCompletedEpisodeFailedTotal = episodeFailedTotal
            } :
            state with { EventsLoading = remainingLoading, EpisodeFailedTotal = 0 };
    }

    [ReducerMethod(typeof(CloseAllLogsAction))]
    public static StatusBarState ReduceCloseAll(StatusBarState state)
    {
        // Closing everything mid-load ends the episode, so the failures counted so far are announced as its total.
        int episodeFailedTotal = state.EpisodeFailedTotal + state.EventsLoading.Values.Sum(progress => progress.Failed);

        return new StatusBarState
        {
            LoadGeneration = state.LoadGeneration + 1,
            FailedEpisodeCompletionCount =
                episodeFailedTotal > 0 ? state.FailedEpisodeCompletionCount + 1 : state.FailedEpisodeCompletionCount,
            LastCompletedEpisodeFailedTotal =
                episodeFailedTotal > 0 ? episodeFailedTotal : state.LastCompletedEpisodeFailedTotal
        };
    }

    [ReducerMethod]
    public static StatusBarState ReduceSetEventsLoading(StatusBarState state, SetEventsLoadingAction action)
    {
        // Progress only updates a live activity; a late tick after the activity was cleared or closed must not resurrect it.
        if (!state.EventsLoading.TryGetValue(action.ActivityId, out var existing) ||
            (existing.Loaded == action.Count && existing.Failed == action.FailedCount))
        {
            return state;
        }

        // Preserve the probe-published Total when only the running counts change.
        return state with
        {
            EventsLoading = state.EventsLoading.SetItem(
                action.ActivityId,
                (action.Count, action.FailedCount, existing.Total))
        };
    }

    [ReducerMethod]
    public static StatusBarState ReduceSetLoadingTotal(StatusBarState state, SetLoadingTotalAction action)
    {
        // Only annotate an activity that is still loading; a probe that finishes after the load cleared must not
        // resurrect a "Loading" entry. Preserving the same reference when unchanged suppresses a spurious StateChanged.
        if (!state.EventsLoading.TryGetValue(action.ActivityId, out var existing) || existing.Total == action.Total)
        {
            return state;
        }

        return state with
        {
            EventsLoading = state.EventsLoading.SetItem(
                action.ActivityId,
                (existing.Loaded, existing.Failed, action.Total))
        };
    }

    [ReducerMethod]
    public static StatusBarState
        ReduceSetResolverStatus(StatusBarState state, SetResolverStatusAction action) =>
        action.Status == state.ResolverStatus ? state : state with { ResolverStatus = action.Status };

    [ReducerMethod]
    public static StatusBarState ReduceStartEventsLoading(StatusBarState state, StartEventsLoadingAction action) =>
        action.LoadGeneration != state.LoadGeneration || state.EventsLoading.ContainsKey(action.ActivityId) ?
            state :
            state with { EventsLoading = state.EventsLoading.SetItem(action.ActivityId, (0, 0, null)) };
}
