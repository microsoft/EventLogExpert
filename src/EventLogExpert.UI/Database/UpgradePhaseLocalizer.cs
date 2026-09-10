// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.Database.Upgrade;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Database;

internal static class UpgradePhaseLocalizer
{
    internal static string Describe(IStringLocalizer<SharedResource> localizer, UpgradePhase phase) => phase switch
    {
        UpgradePhase.BackingUp => localizer["Db_UpgradePhase_BackingUp"],
        UpgradePhase.MigratingSchema => localizer["Db_UpgradePhase_MigratingSchema"],
        UpgradePhase.Verifying => localizer["Db_UpgradePhase_Verifying"],
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, null)
    };
}
