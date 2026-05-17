// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Database.Upgrade;

public enum UpgradePhase
{
    BackingUp,
    MigratingSchema,
    Verifying
}
