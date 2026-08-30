// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Localization;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Common;

internal static class ResolutionStatusLocalizer
{
    internal static string Display(IStringLocalizer<SharedResource> localizer, EventResolutionStatus status) => status switch
    {
        EventResolutionStatus.Resolved => localizer["ResolutionStatus_Resolved"],
        EventResolutionStatus.NoProvider => localizer["ResolutionStatus_NoProvider"],
        EventResolutionStatus.NoMessage => localizer["ResolutionStatus_NoMessage"],
        EventResolutionStatus.Failed => localizer["ResolutionStatus_Failed"],
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };
}
