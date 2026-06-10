// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.DatabaseTools.Elevation;

namespace EventLogExpert.Runtime.Tests.DatabaseTools.Elevation.TestUtils;

internal sealed class FakeElevatedHelperProcess(Stream pipe, int processId) : IElevatedHelperProcess
{
    private readonly TaskCompletionSource<int> _exitTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Stream _pipe = pipe;
    private int _killed;

    public Action? OnKilled { get; set; }

    public Stream Pipe => _pipe;

    public int ProcessId { get; } = processId;

    public bool SimulateUnkillable { get; set; }

    public bool WasKilled => Volatile.Read(ref _killed) != 0;

    public async ValueTask DisposeAsync()
    {
        _exitTcs.TrySetResult(-1);
        await _pipe.DisposeAsync();
    }

    public bool Kill()
    {
        if (_exitTcs.Task.IsCompleted)
        {
            // Process has already "exited" via SignalExited or a prior Kill — report success per the
            // IElevatedHelperProcess.Kill contract ("true if the process was killed by this call OR was already exited").
            return true;
        }

        if (Interlocked.Exchange(ref _killed, 1) != 0)
        {
            return !SimulateUnkillable;
        }

        try { OnKilled?.Invoke(); } catch { /* tests are responsible for their own callback robustness */ }

        if (SimulateUnkillable)
        {
            // Simulate a wedged high-IL helper: the kill attempt fails and the process keeps running so the exit
            // TCS is intentionally NOT set. The runner is expected to fall back to closing the pipe to unblock.
            return false;
        }

        _exitTcs.TrySetResult(-1);

        return true;
    }

    public void SignalExited(int exitCode) => _exitTcs.TrySetResult(exitCode);

    public Task<int> WaitForExitAsync(CancellationToken cancellationToken) =>
        _exitTcs.Task.WaitAsync(cancellationToken);
}
