// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.StatusBar;

[FeatureState(MaximumStateChangedNotificationsPerSecond = 1)]
public sealed record StatusBarState
{
    public ImmutableDictionary<StatusActivityId, (int Loaded, int Failed, long? Total)> EventsLoading { get; init; } =
        ImmutableDictionary<StatusActivityId, (int Loaded, int Failed, long? Total)>.Empty;

    public ResolverStatus ResolverStatus { get; init; } = ResolverStatus.None;

    /// <summary>Advances on every close-all so loads cancelled by it cannot register a loading activity afterwards.</summary>
    public int LoadGeneration { get; init; }

    /// <summary>The failures accumulated by the in-progress loading episode; zero once it completes.</summary>
    public int EpisodeFailedTotal { get; init; }

    /// <summary>Advances once per loading episode that completed with failures; zero-failure completions never advance it.</summary>
    public int FailedEpisodeCompletionCount { get; init; }

    /// <summary>The total failures of the most recent loading episode that completed with failures.</summary>
    public int LastCompletedEpisodeFailedTotal { get; init; }
}
