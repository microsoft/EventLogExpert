// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Database.Upgrade;
using System.Collections.Frozen;

namespace EventLogExpert.Runtime.Banner;

public sealed record BannerProgressEntry(
    UpgradeBatchId BatchId,
    UpgradeProgressScope Scope,
    int CurrentBatchPosition,
    int CurrentBatchSize,
    string CurrentEntryName,
    UpgradePhase CurrentPhase,
    int QueuedBatchesAfter,
    Action Cancel)
{
    private static readonly FrozenSet<string> s_emptyCaseInsensitive =
        FrozenSet.ToFrozenSet<string>([], StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> BatchFileNames { get; init; } = s_emptyCaseInsensitive;
}
