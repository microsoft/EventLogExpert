// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.Stats;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Common;

internal static class StatsDimensionLocalizer
{
    internal static string Label(IStringLocalizer<SharedResource> localizer, StatsDimension dimension) => dimension switch
    {
        StatsDimension.Source => localizer["Stats_Dimension_Source"],
        StatsDimension.EventId => localizer["Stats_Dimension_EventId"],
        StatsDimension.TaskCategory => localizer["Stats_Dimension_TaskCategory"],
        StatsDimension.User => localizer["Stats_Dimension_User"],
        _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, null)
    };
}
