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

    internal static string DisplayToken(IStringLocalizer<SharedResource> localizer, string token) => token switch
    {
        ResolutionStatusTokens.Resolved => Display(localizer, EventResolutionStatus.Resolved),
        ResolutionStatusTokens.NoProvider => Display(localizer, EventResolutionStatus.NoProvider),
        ResolutionStatusTokens.NoMessage => Display(localizer, EventResolutionStatus.NoMessage),
        ResolutionStatusTokens.Failed => Display(localizer, EventResolutionStatus.Failed),
        _ => token
    };
}
