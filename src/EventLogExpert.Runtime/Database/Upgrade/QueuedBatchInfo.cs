// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Database.Upgrade;

public sealed record QueuedBatchInfo(
    UpgradeBatchId BatchId,
    UpgradeProgressScope Scope,
    IReadOnlySet<string> FileNames,
    Action Cancel);
