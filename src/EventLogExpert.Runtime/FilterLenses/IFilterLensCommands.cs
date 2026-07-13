// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.FilterLenses;

public interface IFilterLensCommands
{
    void ClearLenses();

    void RemoveLens(FilterLens lens);

    /// <summary>
    ///     Pushes a "Show Related by Activity ID" lens narrowing the view to events sharing
    ///     <paramref name="activityId" />. A null id is a no-op, so callers may pass an event's nullable ActivityId directly.
    ///     <paramref name="originLog" /> is the source event's <see cref="ResolvedEvent.OwningLog" /> so the lens auto-clears
    ///     when that log is closed.
    /// </summary>
    void ShowRelatedByActivityId(Guid? activityId, string? originLog = null);
}
