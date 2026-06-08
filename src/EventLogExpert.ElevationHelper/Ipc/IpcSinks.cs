// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Ipc;
using EventLogExpert.DatabaseTools.Common.Operations;

namespace EventLogExpert.ElevationHelper.Ipc;

/// <summary>
///     Helper-side <see cref="IProgress{T}" /> implementation for <see cref="DatabaseToolsLogEntry" />. Each
///     <see cref="Report" /> call wraps the entry in a <see cref="LogEnvelope" /> and writes it to the IPC pipe via the
///     shared semaphore-guarded <see cref="IpcEnvelopeWriter" />.
/// </summary>
/// <remarks>
///     <see cref="DatabaseToolsService" /> reports log entries from worker threads (operations run on <c>Task.Run</c>
///     ). The underlying writer's semaphore serializes the writes so concurrent log entries from different operation
///     phases (or the operation thread + the control reader's fault-emission path) cannot interleave on the wire.
/// </remarks>
internal sealed class IpcLogEntrySink(IpcEnvelopeWriter writer) : IProgress<DatabaseToolsLogEntry>
{
    public void Report(DatabaseToolsLogEntry value)
    {
        // Report is synchronous by contract - block briefly on the writer rather than fire-and-forget so the
        // operation back-pressures naturally when the pipe is congested (i.e., the runner is slow to drain).
        // Using GetAwaiter().GetResult() is acceptable here because the writer is a small async wait on a
        // semaphore + a pipe write; no deadlock surface unless the runner stops reading entirely (in which
        // case the runner's grace-then-kill path resolves it).
        try
        {
            writer.WriteAsync(new LogEnvelope(value.TimestampUtc, value.Level, value.Message), CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            // Pipe closed mid-write (runner gone). Drop the entry; the runner has already moved on.
        }
    }
}

/// <summary>
///     Helper-side <see cref="IProgress{T}" /> implementation for <see cref="DatabaseToolsProgress" />. Same contract
///     as <see cref="IpcLogEntrySink" />.
/// </summary>
internal sealed class IpcProgressSink(IpcEnvelopeWriter writer) : IProgress<DatabaseToolsProgress>
{
    public void Report(DatabaseToolsProgress value)
    {
        try
        {
            writer.WriteAsync(new ProgressEnvelope(value.Processed, value.Total, value.CurrentItem), CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            // Pipe closed mid-write (runner gone). Drop the report.
        }
    }
}
