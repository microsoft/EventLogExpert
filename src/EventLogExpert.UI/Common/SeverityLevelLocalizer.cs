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
        SeverityLevel.Critical => localizer["Severity_Level_Critical"],
        SeverityLevel.Error => localizer["Severity_Level_Error"],
        SeverityLevel.Warning => localizer["Severity_Level_Warning"],
        SeverityLevel.Information => localizer["Severity_Level_Information"],
        SeverityLevel.Verbose => localizer["Severity_Level_Verbose"],
        null => localizer["Severity_Unknown"],
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
    };
}
