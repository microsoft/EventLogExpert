// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Database.Upgrade;

public sealed class UpgradeBatchCompletedEventArgs(UpgradeBatchId batchId, UpgradeBatchResult result, bool wasCancelled) : EventArgs
{
    public UpgradeBatchId BatchId { get; } = batchId;

    public UpgradeBatchResult Result { get; } = result;

    public bool WasCancelled { get; } = wasCancelled;
}
