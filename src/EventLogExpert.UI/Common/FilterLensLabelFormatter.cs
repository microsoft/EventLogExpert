// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Localization;
using EventLogExpert.Runtime.FilterLenses;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Common;

internal static class FilterLensLabelFormatter
{
    public static string Format(IStringLocalizer<SharedResource> localizer, FilterLensLabel label) =>
        label switch
        {
            FilterLensLabel.PropertyComparison(var property, var isEqual, var value) =>
                $"{PropertyName(localizer, property)} {(isEqual ? "=" : "\u2260")} {ValueText(localizer, property, value)}",
            FilterLensLabel.ParentActivity(var activityId) =>
                $"{localizer["FilterLens_ParentActivity"]} = {activityId}",
            FilterLensLabel.TimeRange(var afterLocal, var beforeLocal, var sameDay) => sameDay ?
                $"{afterLocal:T} - {beforeLocal:T}" :
                $"{afterLocal:d} {afterLocal:T} - {beforeLocal:d} {beforeLocal:T}",
            FilterLensLabel.TimeWindow(var centerLocal, var radius) => localizer["FilterLens_Near",
                $"{centerLocal:T}",
                FilterLensLabelText.FormatRadius(radius)].Value,
            _ => throw new ArgumentOutOfRangeException(nameof(label), label, null)
        };

    internal static string PropertyName(IStringLocalizer<SharedResource> localizer, EventProperty property) => property switch
    {
        EventProperty.Id => localizer["FilterLens_Property_Id"],
        EventProperty.ActivityId => localizer["FilterLens_Property_ActivityId"],
        EventProperty.Level => localizer["FilterLens_Property_Level"],
        EventProperty.Keywords => localizer["FilterLens_Property_Keywords"],
        EventProperty.Source => localizer["FilterLens_Property_Source"],
        EventProperty.TaskCategory => localizer["FilterLens_Property_TaskCategory"],
        EventProperty.ProcessId => localizer["FilterLens_Property_ProcessId"],
        EventProperty.ThreadId => localizer["FilterLens_Property_ThreadId"],
        EventProperty.UserId => localizer["FilterLens_Property_UserId"],
        EventProperty.Description => localizer["FilterLens_Property_Description"],
        EventProperty.Xml => localizer["FilterLens_Property_Xml"],
        EventProperty.LogName => localizer["FilterLens_Property_LogName"],
        EventProperty.EventData => localizer["FilterLens_Property_EventData"],
        EventProperty.UserData => localizer["FilterLens_Property_UserData"],
        EventProperty.Opcode => localizer["FilterLens_Property_Opcode"],
        EventProperty.RelatedActivityId => localizer["FilterLens_Property_RelatedActivityId"],
        EventProperty.UserDisplayName => localizer["FilterLens_Property_UserDisplayName"],
        EventProperty.ResolutionStatus => localizer["FilterLens_Property_ResolutionStatus"],
        _ => throw new ArgumentOutOfRangeException(nameof(property), property, null)
    };

    // ResolutionStatus filter values are stored as frozen tokens; localize the closed set for display while leaving the
    // token untouched. Any other property's value is raw user text and renders verbatim.
    private static string ValueText(IStringLocalizer<SharedResource> localizer, EventProperty property, string value) =>
        property == EventProperty.ResolutionStatus ? ResolutionStatusLocalizer.DisplayToken(localizer, value) : value;
}
