// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Models;

public sealed class UpgradeBatchProgressEventArgs(Guid batchId, int position, string fileName, UpgradePhase phase) : EventArgs
{
    public Guid BatchId { get; } = batchId;

    public string FileName { get; } = fileName;

    public UpgradePhase Phase { get; } = phase;

    /// <summary>One-based index of <see cref="FileName" /> within its batch.</summary>
    public int Position { get; } = position;
}
