// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Database.Upgrade;

public sealed class UpgradeBatchStartedEventArgs(
    UpgradeBatchId batchId,
    UpgradeProgressScope scope,
    int batchSize,
    CancellationTokenSource cts) : EventArgs
{
    private readonly CancellationTokenSource _cts = cts;

    public UpgradeBatchId BatchId { get; } = batchId;

    public int BatchSize { get; } = batchSize;

    public IReadOnlyList<string> FileNames { get; init; } = [];

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
