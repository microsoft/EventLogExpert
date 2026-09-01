// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.Memory;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Common;

internal static class MemoryUsageLevelLocalizer
{
    internal static string Announcement(IStringLocalizer<SharedResource> localizer, MemoryUsageLevel level) => level switch
    {
        MemoryUsageLevel.Normal => localizer["StatusBar_Memory_Announce_Normal"],
        MemoryUsageLevel.Elevated => localizer["StatusBar_Memory_Announce_Elevated"],
        MemoryUsageLevel.High => localizer["StatusBar_Memory_Announce_High"],
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
    };
}
