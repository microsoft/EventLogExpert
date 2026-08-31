// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Localization;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Common;

internal static class SeverityLevelLocalizer
{
    internal static string Label(IStringLocalizer<SharedResource> localizer, SeverityLevel? level) => level switch
    {
        SeverityLevel.Critical => localizer["Coverage_SeverityLevel_Critical"],
        SeverityLevel.Error => localizer["Coverage_SeverityLevel_Error"],
        SeverityLevel.Warning => localizer["Coverage_SeverityLevel_Warning"],
        SeverityLevel.Information => localizer["Coverage_SeverityLevel_Information"],
        SeverityLevel.Verbose => localizer["Coverage_SeverityLevel_Verbose"],
        null => localizer["Coverage_SeverityUnknown"],
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
    };
}
