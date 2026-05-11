// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Database.Upgrade;

public readonly record struct UpgradeBatchId(Guid Value)
{
    public static UpgradeBatchId Create() => new(Guid.NewGuid());
}
