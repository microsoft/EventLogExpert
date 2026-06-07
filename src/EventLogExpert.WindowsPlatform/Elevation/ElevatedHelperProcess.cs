// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.DatabaseTools.Elevation;
using System.Diagnostics;
using System.IO.Pipes;

namespace EventLogExpert.WindowsPlatform.Elevation;

/// <summary>
///     Concrete <see cref="IElevatedHelperProcess" /> wrapping a spawned helper <see cref="Process" /> + its
///     connected <see cref="NamedPipeServerStream" />. Owned by <see cref="ElevatedHelperProcessHost" />; callers receive
///     it from <c>StartAsync</c> and dispose when done. Disposal closes the pipe (which signals the helper to exit if it's
///     still listening) but does NOT kill the helper — callers SHOULD await exit or call <see cref="Kill" /> first.
/// </summary>
internal sealed class ElevatedHelperProcess(Process process, NamedPipeServerStream pipe, ITraceLogger logger) : IElevatedHelperProcess
{
    private int _disposed;

    public Stream Pipe => pipe;

    public int ProcessId { get; } = process.Id;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }

        try { await pipe.DisposeAsync(); } catch { /* swallowed during dispose */ }

        try { process.Dispose(); } catch { /* swallowed during dispose */ }
    }

    public void Kill()
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: false);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited — no-op.
        }
        catch (Exception ex)
        {
            logger.Warning($"{nameof(ElevatedHelperProcess)}.{nameof(Kill)} threw: {ex}");
        }
    }

    public Task<int> WaitForExitAsync(CancellationToken cancellationToken)
    {
        return WaitAsync();

        async Task<int> WaitAsync()
        {
            await process.WaitForExitAsync(cancellationToken);

            return process.ExitCode;
        }
    }
}
