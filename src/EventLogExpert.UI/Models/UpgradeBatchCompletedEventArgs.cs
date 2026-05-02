// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Models;

public sealed class UpgradeBatchCompletedEventArgs(Guid batchId, UpgradeBatchResult result, bool wasCancelled) : EventArgs
{
    public Guid BatchId { get; } = batchId;

    public UpgradeBatchResult Result { get; } = result;

    public bool WasCancelled { get; } = wasCancelled;
}
