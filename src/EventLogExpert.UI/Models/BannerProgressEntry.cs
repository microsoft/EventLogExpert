// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Models;

public sealed record BannerProgressEntry(
    Guid BatchId,
    UpgradeProgressScope Scope,
    int CurrentBatchPosition,
    int CurrentBatchSize,
    string CurrentEntryName,
    UpgradePhase CurrentPhase,
    int QueuedBatchesAfter,
    Action Cancel);
