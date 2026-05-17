// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Database.Upgrade;

namespace EventLogExpert.Runtime.Banner;

public sealed record BannerProgressEntry(
    UpgradeBatchId BatchId,
    UpgradeProgressScope Scope,
    int CurrentBatchPosition,
    int CurrentBatchSize,
    string CurrentEntryName,
    UpgradePhase CurrentPhase,
    int QueuedBatchesAfter,
    Action Cancel);
