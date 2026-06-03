// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.DatabaseTools.Elevation;

/// <summary>
///     Handle to a running elevation-helper process and its connected duplex pipe. Disposing the handle SHOULD close
///     the pipe and dispose the underlying process handle; callers are responsible for ensuring helper exit before
///     disposing (via <see cref="WaitForExitAsync" /> or <see cref="Kill" />) so no orphan processes remain.
/// </summary>
/// <remarks>
///     <para>
///         <b>Concurrency model for <see cref="Pipe" />:</b> the underlying duplex named pipe has independent OS-level
///         buffers per direction. A SINGLE reader on the incoming direction concurrent with a SINGLE writer on the
///         outgoing direction is supported by the .NET async pipe-stream implementation (overlapped I/O via
///         <c>ThreadPoolBoundHandle</c>) — used by <c>ElevatedDatabaseToolsRunner</c> which simultaneously (a) reads
///         envelopes from the helper and (b) writes <c>CancelEnvelope</c> on cancellation. Concurrent READS on the same
///         direction (or concurrent WRITES on the same direction) are NOT supported — callers must serialize
///         same-direction operations (e.g., with a <see cref="SemaphoreSlim" />).
///     </para>
///     <para>
///         <b>Cleanup contract:</b> implementations of <see cref="DisposeAsync" /> close the pipe but do NOT kill the
///         helper process. Closing the pipe will cause the helper to observe EOF on its next read/write and typically
///         exit; if it doesn't, the caller must invoke <see cref="Kill" /> explicitly. This split avoids surprising the
///         caller when a deliberate keep-alive-after-close pattern is desired (e.g., in tests that want to inspect the
///         helper's exit code separately from pipe teardown).
///     </para>
/// </remarks>
public interface IElevatedHelperProcess : IAsyncDisposable
{
    /// <summary>
    ///     The duplex pipe stream connecting runner ↔ helper. The runner reads envelopes via this stream and writes
    ///     control envelopes (cancel) through it. Implementations guarantee the stream is connected and authenticated before
    ///     this property is first observed. See remarks on the interface for the concurrency model.
    /// </summary>
    Stream Pipe { get; }

    /// <summary>OS process id of the connected helper. Stable for the lifetime of this handle.</summary>
    int ProcessId { get; }

    /// <summary>
    ///     Hard-terminates the helper process. Used as the last-resort cancellation fallback when the cooperative
    ///     <c>CancelEnvelope</c> + grace window does not produce a clean exit. Does NOT throw if the process has already
    ///     exited.
    /// </summary>
    void Kill();

    /// <summary>Waits for the helper process to exit. Returns the OS exit code.</summary>
    Task<int> WaitForExitAsync(CancellationToken cancellationToken);
}
