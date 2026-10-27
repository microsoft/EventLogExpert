// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.StatusBar;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.StatusBar;
using EventLogExpert.UI.Tests.TestUtils;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.StatusBar;

public sealed class FailedCountAnnouncementStateTests
{
    private readonly MarkerLocalizer _localizer = new();

    [Fact]
    public void CompletionAndTheNextEpisodesFirstFailureInOneObservation_AnnouncesTheCompletionFirst()
    {
        FailedCountAnnouncementState state = new();
        state.Initialize(Status());
        var completedWithNextFailing = Status(completedEpisodeCount: 1, lastCompletedFailedTotal: 3, liveFailedCounts: [2]);

        Assert.Equal("[[StatusBar_Loading_Failed(3)]]", state.Update(completedWithNextFailing, _localizer));
        Assert.Equal("[[StatusBar_Loading_Failed(2)]]", state.Update(completedWithNextFailing, _localizer));
    }

    [Fact]
    public void EpisodeCompletion_AnnouncesItsTotalWithTheRepeatMarkerAndStaysStable()
    {
        FailedCountAnnouncementState state = new();
        state.Initialize(Status(liveFailedCounts: [0]));
        Assert.Equal("[[StatusBar_Loading_Failed(3)]]", state.Update(Status(liveFailedCounts: [3]), _localizer));

        var completed = Status(completedEpisodeCount: 1, lastCompletedFailedTotal: 3);

        Assert.Equal("[[StatusBar_Loading_Failed(3)]]\u200B", state.Update(completed, _localizer));
        Assert.Equal("[[StatusBar_Loading_Failed(3)]]\u200B", state.Update(completed, _localizer));
    }

    [Fact]
    public void EpisodeCompletion_WithoutFailures_AnnouncesNothing()
    {
        FailedCountAnnouncementState state = new();
        state.Initialize(Status(liveFailedCounts: [0]));

        Assert.Null(state.Update(Status(), _localizer));
    }

    [Fact]
    public void FailedCompletionFollowedByAFailureFreeEpisode_AnnouncesTheFailedTotalAndThenTheNextFirstFailureOnce()
    {
        FailedCountAnnouncementState state = new();
        state.Initialize(Status());
        Assert.Equal("[[StatusBar_Loading_Failed(3)]]", state.Update(Status(liveFailedCounts: [3]), _localizer));

        var failureFreeEpisodeDone = Status(completedEpisodeCount: 1, lastCompletedFailedTotal: 3);

        Assert.Equal("[[StatusBar_Loading_Failed(3)]]\u200B", state.Update(failureFreeEpisodeDone, _localizer));
        Assert.Equal("[[StatusBar_Loading_Failed(3)]]\u200B", state.Update(failureFreeEpisodeDone with { LoadingActivities = Loading(0) }, _localizer));

        var nextFailing = failureFreeEpisodeDone with { LoadingActivities = Loading(2) };

        Assert.Equal("[[StatusBar_Loading_Failed(2)]]", state.Update(nextFailing, _localizer));
        Assert.Equal("[[StatusBar_Loading_Failed(2)]]", state.Update(nextFailing, _localizer));
    }

    [Fact]
    public void FailedCompletionInTheSameObservationAsTheNextFailure_RearmsAndAnnouncesTheNextFailureOnce()
    {
        FailedCountAnnouncementState state = new();
        state.Initialize(Status());
        Assert.Equal("[[StatusBar_Loading_Failed(3)]]", state.Update(Status(liveFailedCounts: [3]), _localizer));

        var completedWithNextFailing = Status(completedEpisodeCount: 1, lastCompletedFailedTotal: 3, liveFailedCounts: [2]);

        Assert.Equal("[[StatusBar_Loading_Failed(3)]]\u200B", state.Update(completedWithNextFailing, _localizer));
        Assert.Equal("[[StatusBar_Loading_Failed(2)]]", state.Update(completedWithNextFailing, _localizer));
        Assert.Equal("[[StatusBar_Loading_Failed(2)]]", state.Update(completedWithNextFailing, _localizer));
    }

    [Fact]
    public void FailedCount_FormatsThousandsLikeTheVisibleChipAtArrivalAndCompletion()
    {
        FailedCountAnnouncementState state = new();
        state.Initialize(Status(liveFailedCounts: [0]));
        string expected = StatusBarTextComposer.LoadingFailedText(_localizer, 1234);

        Assert.Equal(expected, state.Update(Status(liveFailedCounts: [1234]), _localizer));
        Assert.Equal(expected + "\u200B", state.Update(Status(completedEpisodeCount: 1, lastCompletedFailedTotal: 1234), _localizer));
    }

    [Fact]
    public void FirstFailureInAnEpisode_AnnouncesTheEpisodeTotalIncludingFinishedActivitiesOnce()
    {
        FailedCountAnnouncementState state = new();
        state.Initialize(Status(liveFailedCounts: [0, 0]));

        Assert.Null(state.Update(Status(liveFailedCounts: [0, 0]), _localizer));
        Assert.Equal("[[StatusBar_Loading_Failed(3)]]", state.Update(Status(episodeFailedTotal: 1, liveFailedCounts: [2, 0]), _localizer));
        Assert.Equal("[[StatusBar_Loading_Failed(3)]]", state.Update(Status(episodeFailedTotal: 1, liveFailedCounts: [5, 4]), _localizer));
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(4, 0)]
    public void InitializationDuringAnEpisode_DoesNotReplayItsFailuresButAnnouncesItsCompletion(int episodeFailedTotal, int liveFailedCount)
    {
        FailedCountAnnouncementState state = new();
        var mounted = Status(completedEpisodeCount: 5, lastCompletedFailedTotal: 9, episodeFailedTotal: episodeFailedTotal, liveFailedCounts: [liveFailedCount]);
        state.Initialize(mounted);

        Assert.Null(state.Update(mounted, _localizer));
        Assert.Null(state.Update(mounted with { EpisodeFailedTotal = episodeFailedTotal + 1 }, _localizer));
        Assert.Equal("[[StatusBar_Loading_Failed(7)]]", state.Update(Status(completedEpisodeCount: 6, lastCompletedFailedTotal: 7), _localizer));
    }

    [Fact]
    public void NewEpisode_AfterCompletion_RearmsTheFirstFailureAnnouncement()
    {
        FailedCountAnnouncementState state = new();
        state.Initialize(Status(liveFailedCounts: [0]));
        Assert.Equal("[[StatusBar_Loading_Failed(3)]]", state.Update(Status(liveFailedCounts: [3]), _localizer));
        Assert.Equal("[[StatusBar_Loading_Failed(3)]]\u200B", state.Update(Status(completedEpisodeCount: 1, lastCompletedFailedTotal: 3), _localizer));

        Assert.Equal("[[StatusBar_Loading_Failed(3)]]\u200B", state.Update(Status(completedEpisodeCount: 1, lastCompletedFailedTotal: 3, liveFailedCounts: [0]), _localizer));
        Assert.Equal("[[StatusBar_Loading_Failed(2)]]", state.Update(Status(completedEpisodeCount: 1, lastCompletedFailedTotal: 3, liveFailedCounts: [2]), _localizer));
        Assert.Equal("[[StatusBar_Loading_Failed(2)]]\u200B", state.Update(Status(completedEpisodeCount: 2, lastCompletedFailedTotal: 2), _localizer));
    }

    [Fact]
    public void PhantomZeroCompletionAfterAFailedCompletionWithinOneObservation_StillAnnouncesTheFailedTotal()
    {
        FailedCountAnnouncementState state = new();
        state.Initialize(Status());

        Assert.Equal("[[StatusBar_Loading_Failed(3)]]", state.Update(Status(completedEpisodeCount: 1, lastCompletedFailedTotal: 3), _localizer));
    }

    [Fact]
    public void RemountAfterACompletion_DoesNotReplayIt()
    {
        FailedCountAnnouncementState state = new();
        var completed = Status(completedEpisodeCount: 2, lastCompletedFailedTotal: 3);
        state.Initialize(completed);

        Assert.Null(state.Update(completed, _localizer));
        Assert.Null(state.Announcement);
    }

    [Fact]
    public void RemountAfterAFailedCompletionFollowedByAFailureFreeEpisode_DoesNotReplayIt()
    {
        FailedCountAnnouncementState state = new();
        var mounted = Status(completedEpisodeCount: 1, lastCompletedFailedTotal: 3, liveFailedCounts: [0]);
        state.Initialize(mounted);

        Assert.Null(state.Update(mounted, _localizer));
        Assert.Equal("[[StatusBar_Loading_Failed(2)]]", state.Update(mounted with { LoadingActivities = Loading(2) }, _localizer));
    }

    [Fact]
    public void SeveralEpisodesCompletingBetweenObservations_AnnounceOnlyTheLatestTotal()
    {
        FailedCountAnnouncementState state = new();
        state.Initialize(Status(liveFailedCounts: [0]));

        Assert.Equal("[[StatusBar_Loading_Failed(4)]]", state.Update(Status(completedEpisodeCount: 3, lastCompletedFailedTotal: 4), _localizer));
    }

    [Fact]
    public void UpdateBeforeInitialization_SeedsWithoutAnnouncing()
    {
        FailedCountAnnouncementState state = new();
        var completed = Status(completedEpisodeCount: 1, lastCompletedFailedTotal: 3, liveFailedCounts: [2]);

        Assert.Null(state.Update(completed, _localizer));
        Assert.Null(state.Update(completed, _localizer));
    }

    [Fact]
    public void ZeroFailureTotalAfterAnAnnouncedEpisode_RearmsTheFirstFailureAnnouncement()
    {
        FailedCountAnnouncementState state = new();
        state.Initialize(Status());
        Assert.Equal("[[StatusBar_Loading_Failed(3)]]", state.Update(Status(liveFailedCounts: [3]), _localizer));

        Assert.Equal("[[StatusBar_Loading_Failed(3)]]", state.Update(Status(liveFailedCounts: [0]), _localizer));
        Assert.Equal("[[StatusBar_Loading_Failed(2)]]", state.Update(Status(liveFailedCounts: [2]), _localizer));
    }

    private static ImmutableDictionary<StatusActivityId, LoadingProgress> Loading(int failedCount) =>
        ImmutableDictionary<StatusActivityId, LoadingProgress>.Empty.Add(StatusActivityId.Create(), new LoadingProgress(0, failedCount));

    private static StatusBarPresentation Status(
        int completedEpisodeCount = 0,
        int lastCompletedFailedTotal = 0,
        int episodeFailedTotal = 0,
        int[]? liveFailedCounts = null) =>
        new()
        {
            FailedEpisodeCompletionCount = completedEpisodeCount,
            LastCompletedEpisodeFailedTotal = lastCompletedFailedTotal,
            EpisodeFailedTotal = episodeFailedTotal,
            LoadingActivities = (liveFailedCounts ?? []).ToImmutableDictionary(
                _ => StatusActivityId.Create(),
                failedCount => new LoadingProgress(0, failedCount))
        };
}
