// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Ipc;
using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.ElevationHelper.Ipc;

/// <summary>
///     Helper-side <see cref="IProgress{T}" /> implementation for <see cref="LogRecord" />. Each
///     <see cref="Report" /> call wraps the entry in a <see cref="LogMessage" /> and writes it to the IPC pipe via the
///     shared semaphore-guarded <see cref="IpcMessageWriter" />.
/// </summary>
/// <remarks>
///     <see cref="DatabaseToolsService" /> reports log entries from worker threads (operations run on <c>Task.Run</c>
///     ). The underlying writer's semaphore serializes the writes so concurrent log entries from different operation
///     phases (or the operation thread + the control reader's fault-emission path) cannot interleave on the wire.
/// </remarks>
internal sealed class IpcLogForwarder(IpcMessageWriter writer) : IProgress<LogRecord>
{
    public void Report(LogRecord value)
    {
        // Report is synchronous by contract - block briefly on the writer rather than fire-and-forget so the
        // operation back-pressures naturally when the pipe is congested (i.e., the runner is slow to drain).
        // Using GetAwaiter().GetResult() is acceptable here because the writer is a small async wait on a
        // semaphore + a pipe write; no deadlock surface unless the runner stops reading entirely (in which
        // case the runner's grace-then-kill path resolves it).
        try
        {
            writer.WriteAsync(
                    // Re-stamp origin at the IPC boundary: helper LogRecords carry ProcessOrigin.InProcess by
                    // default (StreamingTraceLogger does not set it), so pass ElevatedHelper explicitly - forwarding
                    // value.ProcessOrigin would mis-tag every helper line as in-process.
                    new LogMessage(value.TimestampUtc, value.Level, value.Message, value.Category, ProcessOrigin.ElevatedHelper),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            // Pipe closed mid-write (runner gone). Drop the entry; the runner has already moved on.
        }
    }
}
