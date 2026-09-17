// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.DatabaseTools;

internal static class DatabaseToolsTabLocalizer
{
    internal static string Label(IStringLocalizer<SharedResource> localizer, DatabaseToolsTab tab) => tab switch
    {
        DatabaseToolsTab.Manage => localizer["DatabaseToolsTab_Manage"],
        DatabaseToolsTab.Show => localizer["DatabaseToolsTab_Show"],
        DatabaseToolsTab.Create => localizer["DatabaseToolsTab_Create"],
        DatabaseToolsTab.Merge => localizer["DatabaseToolsTab_Merge"],
        DatabaseToolsTab.Diff => localizer["DatabaseToolsTab_Diff"],
        DatabaseToolsTab.Upgrade => localizer["DatabaseToolsTab_Upgrade"],
        _ => throw new ArgumentOutOfRangeException(nameof(tab), tab, null)
    };
}
