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

    public bool WasKilled => Volatile.Read(ref _killed) != 0;

    public async ValueTask DisposeAsync()
    {
        _exitTcs.TrySetResult(-1);
        await _pipe.DisposeAsync();
    }

    public void Kill()
    {
        if (Interlocked.Exchange(ref _killed, 1) == 0)
        {
            try { OnKilled?.Invoke(); } catch { /* tests are responsible for their own callback robustness */ }
            _exitTcs.TrySetResult(-1);
        }
    }

    public void SignalExited(int exitCode) => _exitTcs.TrySetResult(exitCode);

    public Task<int> WaitForExitAsync(CancellationToken cancellationToken) =>
        _exitTcs.Task.WaitAsync(cancellationToken);
}
