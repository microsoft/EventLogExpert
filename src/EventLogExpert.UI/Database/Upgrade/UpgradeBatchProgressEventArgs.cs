// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Database.Upgrade;

public sealed class UpgradeBatchProgressEventArgs(Guid batchId, int position, string fileName, UpgradePhase phase) : EventArgs
{
    public Guid BatchId { get; } = batchId;

    public string FileName { get; } = fileName;

    public UpgradePhase Phase { get; } = phase;

    public int Position { get; } = position;
}
