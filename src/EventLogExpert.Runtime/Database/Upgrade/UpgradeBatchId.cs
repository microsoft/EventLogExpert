// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Database.Upgrade;

public readonly record struct UpgradeBatchId(Guid Value)
{
    public static UpgradeBatchId Create() => new(Guid.NewGuid());
}
