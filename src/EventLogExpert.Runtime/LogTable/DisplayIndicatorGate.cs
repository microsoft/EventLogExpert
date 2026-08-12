// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.LogTable;

public sealed class DisplayIndicatorGate : IDisposable
{
    private const int ActiveIndicatorHistory = 16;
    public static readonly TimeSpan OnsetDelay = TimeSpan.FromMilliseconds(200);
    private readonly List<ActiveIndicator> _activeIndicators = [];

    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Lock _gate = new();
    private readonly IOrderedViewSource _source;

    private bool _disposed;
    private long _generation;
    private CancellationTokenSource? _onset;

    public DisplayIndicatorGate(IOrderedViewSource source, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _source = source;
        _delay = delay ?? Task.Delay;

        _source.Updated += Observe;

        Observe(_source.Current);
    }

    public event Action? OnsetElapsed;

    public void Dispose()
    {
        CancellationTokenSource? onset;

        lock (_gate)
        {
            if (_disposed) { return; }

            _disposed = true;
            onset = _onset;
            _onset = null;
        }

        _source.Updated -= Observe;

        onset?.Cancel();
        onset?.Dispose();
    }

    public bool IsFiredFor(DisplayIndicatorKind kind, long paintedRevision)
    {
        if (kind == DisplayIndicatorKind.None) { return false; }

        lock (_gate)
        {
            foreach (var indicator in _activeIndicators)
            {
                if (indicator.Kind == kind && indicator.ArmedRevision <= paintedRevision) { return indicator.Fired; }
            }

            return false;
        }
    }

    private void Observe(OrderedViewPresentation presentation)
    {
        CancellationTokenSource? superseded;
        ActiveIndicator armed;
        CancellationToken onsetToken;

        lock (_gate)
        {
            if (_disposed) { return; }

            var kind = presentation.IndicatorKind;

            if (_activeIndicators.Count > 0 && _activeIndicators[0].Kind == kind) { return; }

            superseded = _onset;
            _onset = null;

            armed = new ActiveIndicator(kind, ++_generation, presentation.Revision);

            _activeIndicators.Insert(0, armed);

            if (_activeIndicators.Count > ActiveIndicatorHistory) { _activeIndicators.RemoveAt(_activeIndicators.Count - 1); }

            if (kind == DisplayIndicatorKind.None)
            {
                superseded?.Cancel();
                superseded?.Dispose();

                return;
            }

            _onset = new CancellationTokenSource();
            onsetToken = _onset.Token;
        }

        superseded?.Cancel();
        superseded?.Dispose();

        _ = RunOnsetAsync(armed, onsetToken);
    }

    private async Task RunOnsetAsync(ActiveIndicator indicator, CancellationToken token)
    {
        try
        {
            await _delay(OnsetDelay, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return; }
        catch (ObjectDisposedException)
        {
            /* The onset CTS was disposed by a concurrent Dispose()/supersede; treat as cancelled. */
            return;
        }

        lock (_gate)
        {
            if (_disposed) { return; }

            if (_activeIndicators.Count == 0 || _activeIndicators[0].Generation != indicator.Generation) { return; }

            indicator.Fired = true;
        }

        OnsetElapsed?.Invoke();
    }

    private sealed class ActiveIndicator(DisplayIndicatorKind kind, long generation, long armedRevision)
    {
        public long ArmedRevision { get; } = armedRevision;

        public bool Fired { get; set; }

        public long Generation { get; } = generation;

        public DisplayIndicatorKind Kind { get; } = kind;
    }
}
