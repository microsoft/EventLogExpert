// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Database.Upgrade;

namespace EventLogExpert.UI.Banner;

public sealed record BannerProgressEntry(
    Guid BatchId,
    UpgradeProgressScope Scope,
    int CurrentBatchPosition,
    int CurrentBatchSize,
    string CurrentEntryName,
    UpgradePhase CurrentPhase,
    int QueuedBatchesAfter,
    Action Cancel);
