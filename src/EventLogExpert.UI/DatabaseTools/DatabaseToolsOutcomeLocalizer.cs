// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.Localization;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.DatabaseTools;

internal static class DatabaseToolsOutcomeLocalizer
{
    internal static string Label(IStringLocalizer<SharedResource> localizer, DatabaseToolsOutcome outcome) => outcome switch
    {
        DatabaseToolsOutcome.Succeeded => localizer["DatabaseToolsOutcome_Succeeded"],
        DatabaseToolsOutcome.Cancelled => localizer["DatabaseToolsOutcome_Cancelled"],
        DatabaseToolsOutcome.Failed => localizer["DatabaseToolsOutcome_Failed"],
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null)
    };
}
