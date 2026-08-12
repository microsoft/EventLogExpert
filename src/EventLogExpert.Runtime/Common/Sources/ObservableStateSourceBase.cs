// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using Fluxor;

namespace EventLogExpert.Runtime.Common.Sources;

internal abstract class ObservableStateSourceBase<TState, TProjection> : IChangeNotifier, IDisposable
{
    private static readonly Func<TProjection, TProjection, bool> s_defaultEquals =
        static (next, current) => EqualityComparer<TProjection>.Default.Equals(next, current);

    private readonly Lock _gate = new();
    private readonly ITraceLogger _logger;
    private readonly Func<TState, TProjection> _project;
    private readonly Func<TProjection, TProjection, bool> _projectionEquals;
    private readonly IState<TState> _state;

    private TProjection _current;
    private bool _disposed;

    protected ObservableStateSourceBase(
        IState<TState> state,
        ITraceLogger logger,
        Func<TState, TProjection> project,
        Func<TProjection, TProjection, bool>? projectionEquals = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(project);

        _state = state;
        _logger = logger;
        _project = project;
        _projectionEquals = projectionEquals ?? s_defaultEquals;

        _current = _project(state.Value);
        _state.StateChanged += OnStateChanged;

        lock (_gate) { _current = _project(_state.Value); }
    }

    public event Action? Changed;

    protected TProjection CurrentProjection
    {
        get { lock (_gate) { return _current; } }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) { return; }

            _disposed = true;
        }

        _state.StateChanged -= OnStateChanged;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        var next = _project(_state.Value);

        lock (_gate)
        {
            if (_disposed || _projectionEquals(next, _current)) { return; }

            _current = next;
        }

        RaiseChanged();
    }

    private void RaiseChanged()
    {
        var handlers = Changed;

        if (handlers is null) { return; }

        foreach (var handler in handlers.GetInvocationList().Cast<Action>())
        {
            try
            {
                handler();
            }
            catch (Exception fault)
            {
                _logger.Trace($"{GetType().Name}: a subscriber threw and was isolated: {fault}");
            }
        }
    }
}
