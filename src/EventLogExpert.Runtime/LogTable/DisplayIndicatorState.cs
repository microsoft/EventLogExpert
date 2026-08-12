// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.LogTable;

public sealed class DisplayIndicatorState : IDisposable
{
    public static readonly TimeSpan MinimumVisible = TimeSpan.FromMilliseconds(300);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly DisplayIndicatorGate _gate;
    private readonly Action _requestRender;
    private readonly Lock _sync = new();

    private bool _disposed;
    private CancellationTokenSource? _floor;
    private long _floorGeneration;
    private bool _floorHolding;
    private bool _spinnerOnScreen;

    public DisplayIndicatorState(
        DisplayIndicatorGate gate,
        Action requestRender,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _gate = gate;
        _requestRender = requestRender;
        _delay = delay ?? Task.Delay;

        _gate.OnsetElapsed += OnGateOnsetElapsed;
    }

    public void Dispose()
    {
        CancellationTokenSource? floor;

        lock (_sync)
        {
            if (_disposed) { return; }

            _disposed = true;
            floor = _floor;
            _floor = null;
        }

        _gate.OnsetElapsed -= OnGateOnsetElapsed;

        floor?.Cancel();
        floor?.Dispose();
    }

    private void OnGateOnsetElapsed() => _requestRender();

    public void RecordPaint(DisplayedIndicator painted)
    {
        CancellationTokenSource? superseded = null;
        CancellationToken floorToken = default;
        long generation = 0;
        bool startFloor = false;

        lock (_sync)
        {
            if (_disposed) { return; }

            if (painted.Spinner)
            {
                if (_spinnerOnScreen) { return; }

                _spinnerOnScreen = true;
                _floorHolding = true;
                superseded = _floor;
                _floor = new CancellationTokenSource();
                floorToken = _floor.Token;
                generation = ++_floorGeneration;
                startFloor = true;
            }
            else
            {
                _spinnerOnScreen = false;
                _floorHolding = false;
                superseded = _floor;
                _floor = null;
            }
        }

        superseded?.Cancel();
        superseded?.Dispose();

        if (startFloor) { _ = RunFloorAsync(generation, floorToken); }
    }

    public DisplayedIndicator Resolve(
        DisplayIndicatorKind paintedKind,
        long paintedRevision,
        bool surfaceStillCatchingUp = false)
    {
        if (_gate.IsFiredFor(paintedKind, paintedRevision))
        {
            return new DisplayedIndicator(paintedKind, true);
        }

        bool stillOwed = paintedKind != DisplayIndicatorKind.None;

        lock (_sync)
        {
            if (!_spinnerOnScreen) { return DisplayedIndicator.Nothing; }

            return stillOwed || surfaceStillCatchingUp || _floorHolding ?
                DisplayedIndicator.GenericSpinner :
                DisplayedIndicator.Nothing;
        }
    }

    private async Task RunFloorAsync(long generation, CancellationToken token)
    {
        try
        {
            await _delay(MinimumVisible, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return; }

        lock (_sync)
        {
            if (_disposed || _floorGeneration != generation || !_floorHolding) { return; }

            _floorHolding = false;
        }

        _requestRender();
    }
}
