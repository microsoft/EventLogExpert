// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Common.Filtering;
using Fluxor;
using System.Globalization;

namespace EventLogExpert.Runtime.FilterLenses;

internal sealed class FilterLensCommands(IDispatcher dispatcher) : IFilterLensCommands
{
    private readonly IDispatcher _dispatcher = dispatcher;

    public void ClearLenses() => _dispatcher.Dispatch(new ClearFilterLensesAction());

    public void ExcludeEventId(int eventId, string? originLog = null) =>
        PushLens(FilterLensFactory.ForExcludedValue(EventProperty.Id, eventId.ToString(CultureInfo.InvariantCulture), originLog));

    public void ExcludeValue(EventProperty property, string value, string? originLog = null) =>
        PushLens(FilterLensFactory.ForExcludedValue(property, value, originLog));

    public void IncludeEventId(int eventId, string? originLog = null) =>
        PushLens(FilterLensFactory.ForIncludedValue(EventProperty.Id, eventId.ToString(CultureInfo.InvariantCulture), originLog));

    public void IncludeValue(EventProperty property, string value, string? originLog = null) =>
        PushLens(FilterLensFactory.ForIncludedValue(property, value, originLog));

    public void PromoteLens(FilterLensId id) => _dispatcher.Dispatch(new PromoteFilterLensAction(id));

    public void RemoveLens(FilterLensId id) => _dispatcher.Dispatch(new RemoveFilterLensAction(id));

    public void ShowEventsNearTime(DateTime timeCreated, TimeSpan radius, TimeZoneInfo displayZone, string? originLog = null) =>
        PushLens(FilterLensFactory.ForTimeWindow(timeCreated, radius, displayZone, originLog));

    public void ShowParentActivity(Guid? relatedActivityId, string? originLog = null)
    {
        if (relatedActivityId is { } id)
        {
            PushLens(FilterLensFactory.ForParentActivity(id, originLog));
        }
    }

    public void ShowRelatedByActivityId(Guid? activityId, string? originLog = null)
    {
        if (activityId is { } id) { PushLens(FilterLensFactory.ForActivityId(id, originLog)); }
    }

    public void ShowRelatedByRelatedActivityId(Guid? relatedActivityId, string? originLog = null)
    {
        if (relatedActivityId is { } id) { PushLens(FilterLensFactory.ForRelatedActivityId(id, originLog)); }
    }

    public void ShowTimeRange(DateTime startUtc, DateTime endUtc, TimeZoneInfo displayZone, string? originLog = null) =>
        PushLens(FilterLensFactory.ForTimeRange(startUtc, endUtc, displayZone, originLog));

    private void PushLens(FilterLens? lens)
    {
        if (lens != null) { _dispatcher.Dispatch(new PushFilterLensAction(lens)); }
    }
}
