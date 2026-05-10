// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Models;

public sealed class UpgradeBatchStartedEventArgs(
    Guid batchId,
    UpgradeProgressScope scope,
    int batchSize,
    CancellationTokenSource cts) : EventArgs
{
    private readonly CancellationTokenSource _cts = cts;

    public Guid BatchId { get; } = batchId;

    public int BatchSize { get; } = batchSize;

    public UpgradeProgressScope Scope { get; } = scope;

    public void Cancel()
    {
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Batch already completed; cancellation is moot.
        }
    }
}
