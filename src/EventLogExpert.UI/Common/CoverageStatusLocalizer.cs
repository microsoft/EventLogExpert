// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.ResolutionCoverage;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Common;

internal static class CoverageStatusLocalizer
{
    internal static string Label(IStringLocalizer<SharedResource> localizer, CoverageStatus status) => status switch
    {
        CoverageStatus.Full => localizer["Coverage_Status_Full"],
        CoverageStatus.Partial => localizer["Coverage_Status_Partial"],
        CoverageStatus.None => localizer["Coverage_Status_None"],
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };
}
