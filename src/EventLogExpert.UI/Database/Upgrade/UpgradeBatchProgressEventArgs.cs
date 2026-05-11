// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Database.Upgrade;

public sealed class UpgradeBatchProgressEventArgs(UpgradeBatchId batchId, int position, string fileName, UpgradePhase phase) : EventArgs
{
    public UpgradeBatchId BatchId { get; } = batchId;

    public string FileName { get; } = fileName;

    public UpgradePhase Phase { get; } = phase;

    public int Position { get; } = position;
}
