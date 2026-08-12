// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Common;

public sealed class SourceSubscription : IDisposable
{
    private readonly Func<Task> _render;
    private readonly Action _unsubscribe;

    private volatile bool _disposed;

    public SourceSubscription(Action<Action> subscribe, Action<Action> unsubscribe, Func<Task> render)
    {
        ArgumentNullException.ThrowIfNull(subscribe);
        ArgumentNullException.ThrowIfNull(unsubscribe);
        ArgumentNullException.ThrowIfNull(render);

        _render = render;
        _unsubscribe = () => unsubscribe(OnChanged);
        subscribe(OnChanged);
    }

    public void Dispose()
    {
        if (_disposed) { return; }

        _disposed = true;
        _unsubscribe();
    }

    // that lands during teardown (the source is a singleton that outlives this component).
    private void OnChanged()
    {
        if (_disposed) { return; }

        _ = RenderAsync();
    }

    private async Task RenderAsync()
    {
        if (_disposed) { return; }

        try { await _render(); }
        catch (ObjectDisposedException) { }
        catch (OperationCanceledException) { }
    }
}
