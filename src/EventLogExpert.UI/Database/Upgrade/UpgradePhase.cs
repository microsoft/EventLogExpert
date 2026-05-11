// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Database.Upgrade;

public enum UpgradePhase
{
    BackingUp,
    MigratingSchema,
    Verifying
}
