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

    /// <summary>
    ///     Cancels the batch by signaling the underlying <see cref="CancellationTokenSource" />. Safe to call after the
    ///     batch has completed: the underlying source may already be disposed by the consumer task, in which case the
    ///     resulting <see cref="ObjectDisposedException" /> is swallowed because cancellation arriving after completion is a
    ///     no-op.
    /// </summary>
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
