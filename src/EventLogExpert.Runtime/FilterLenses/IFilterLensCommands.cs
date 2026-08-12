// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.FilterLenses;

public interface IFilterLensCommands
{
    void ClearLenses();

    void RemoveLens(FilterLensId id);

    void ShowEventsNearTime(DateTime timeCreated, TimeSpan radius, TimeZoneInfo displayZone, string? originLog = null);

    void ShowParentActivity(Guid? relatedActivityId, string? originLog = null);

    void ShowRelatedByActivityId(Guid? activityId, string? originLog = null);

    void ShowRelatedByRelatedActivityId(Guid? relatedActivityId, string? originLog = null);

    void ShowTimeRange(DateTime startUtc, DateTime endUtc, TimeZoneInfo displayZone, string? originLog = null);
}
