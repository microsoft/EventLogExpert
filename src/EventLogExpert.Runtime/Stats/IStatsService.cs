// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.LogTable;

namespace EventLogExpert.Runtime.Stats;

/// <summary>
///     Computes statistics over a supplied filtered view. Stateless and synchronous - the caller owns threading,
///     throttling, and supersession (the statistics panel runs these off the UI thread with cancellation).
/// </summary>
public interface IStatsService
{
    DimensionStats BuildDimension(IEventColumnView view, StatsDimension dimension, int topN, CancellationToken cancellationToken);

    SeverityStats BuildSeverity(IEventColumnView view, CancellationToken cancellationToken);
}
