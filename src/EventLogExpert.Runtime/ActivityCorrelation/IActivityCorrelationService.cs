// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.ActivityCorrelation;

/// <summary>
///     Builds the within-log activity correlation neighborhood of a selected event on demand from the raw columnar
///     store. The build is topology-only (no per-row string materialization) and runs off the caller's thread.
/// </summary>
public interface IActivityCorrelationService
{
    /// <summary>
    ///     Builds the correlation neighborhood rooted on <paramref name="focusedEvent" />, or returns <c>null</c> when
    ///     the event's log is not loaded or the locator is stale against the current snapshot. A returned view is
    ///     <see cref="ActivityCorrelationView.IsEmpty" /> when the event carries no usable <c>ActivityId</c>.
    /// </summary>
    Task<ActivityCorrelationView?> BuildAsync(EventLocator focusedEvent, CancellationToken cancellationToken);

    /// <summary>
    ///     Reads the current snapshot identity for <paramref name="logId" /> so a caller can detect that a previously
    ///     built view has gone stale (a live-tail append bumps <c>ContentVersion</c>). Returns <c>false</c> when the log is
    ///     not loaded.
    /// </summary>
    bool TryGetContentToken(EventLogId logId, out CorrelationContentToken token);
}
