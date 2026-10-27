// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.StatusBar;
using EventLogExpert.UI.Common;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.StatusBar;

internal sealed class FailedCountAnnouncementState
{
    private string? _announcement;
    private bool _currentEpisodeFailureAnnounced;
    private bool _initialized;
    private int _observedFailedEpisodeCompletionCount;

    internal string? Announcement => _announcement;

    internal void Initialize(StatusBarPresentation status)
    {
        _announcement = null;
        _observedFailedEpisodeCompletionCount = status.FailedEpisodeCompletionCount;
        _currentEpisodeFailureAnnounced = CurrentEpisodeFailedTotal(status) > 0;
        _initialized = true;
    }

    internal string? Update(StatusBarPresentation status, IStringLocalizer<SharedResource> localizer)
    {
        if (!_initialized)
        {
            Initialize(status);

            return null;
        }

        string? announcement = null;

        // When several episodes complete between observations only the latest total is still available to announce.
        if (status.FailedEpisodeCompletionCount > _observedFailedEpisodeCompletionCount)
        {
            _observedFailedEpisodeCompletionCount = status.FailedEpisodeCompletionCount;
            _currentEpisodeFailureAnnounced = false;
            announcement = StatusBarTextComposer.LoadingFailedText(localizer, status.LastCompletedEpisodeFailedTotal);
        }

        int currentEpisodeFailedTotal = CurrentEpisodeFailedTotal(status);

        // A zero-failure episode never advances the completion counter, so a zero total is what re-arms the next first failure.
        if (currentEpisodeFailedTotal == 0)
        {
            _currentEpisodeFailureAnnounced = false;
        }

        if (announcement is null && !_currentEpisodeFailureAnnounced && currentEpisodeFailedTotal > 0)
        {
            _currentEpisodeFailureAnnounced = true;
            announcement = StatusBarTextComposer.LoadingFailedText(localizer, currentEpisodeFailedTotal);
        }

        if (announcement is not null)
        {
            _announcement = announcement == _announcement ? announcement + "\u200B" : announcement;
        }

        return _announcement;
    }

    private static int CurrentEpisodeFailedTotal(StatusBarPresentation status) =>
        status.EpisodeFailedTotal + status.LoadingActivities.Values.Sum(progress => progress.Failed);
}
