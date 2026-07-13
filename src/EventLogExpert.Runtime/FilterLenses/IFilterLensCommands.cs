// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.FilterLenses;

public interface IFilterLensCommands
{
    void ClearLenses();

    void RemoveLens(FilterLens lens);

    /// <summary>
    ///     Pushes a transient time-window lens centered on <paramref name="timeCreated" /> (the source event's UTC
    ///     timestamp), narrowing the view to events no more than <paramref name="radius" /> before or after it. The window is
    ///     inclusive, so the source event always stays in view. <paramref name="displayZone" /> renders the chip's anchor time
    ///     in the grid's display zone; <paramref name="originLog" /> is the source event's
    ///     <see cref="ResolvedEvent.OwningLog" /> so the lens auto-clears when that log is closed.
    /// </summary>
    void ShowEventsNearTime(DateTime timeCreated, TimeSpan radius, TimeZoneInfo displayZone, string? originLog = null);

    /// <summary>
    ///     Pushes a lens narrowing the view to the parent activity's events - those whose ActivityId equals the source
    ///     event's <paramref name="relatedActivityId" />. A null id is a no-op. <paramref name="originLog" /> is the source
    ///     event's <see cref="ResolvedEvent.OwningLog" /> so the lens auto-clears when that log is closed.
    /// </summary>
    void ShowParentActivity(Guid? relatedActivityId, string? originLog = null);

    /// <summary>
    ///     Pushes a "Show Related by Activity ID" lens narrowing the view to events sharing
    ///     <paramref name="activityId" />. A null id is a no-op, so callers may pass an event's nullable ActivityId directly.
    ///     <paramref name="originLog" /> is the source event's <see cref="ResolvedEvent.OwningLog" /> so the lens auto-clears
    ///     when that log is closed.
    /// </summary>
    void ShowRelatedByActivityId(Guid? activityId, string? originLog = null);

    /// <summary>
    ///     Pushes a lens narrowing the view to events that share <paramref name="relatedActivityId" /> (siblings of the
    ///     same parent/correlation activity). A null id is a no-op. <paramref name="originLog" /> is the source event's
    ///     <see cref="ResolvedEvent.OwningLog" /> so the lens auto-clears when that log is closed.
    /// </summary>
    void ShowRelatedByRelatedActivityId(Guid? relatedActivityId, string? originLog = null);
}
