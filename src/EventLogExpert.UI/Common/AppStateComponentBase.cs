// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Sources;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Common;

public abstract class AppStateComponentBase : ComponentBase, IAsyncDisposable
{
    private readonly List<SourceSubscription> _subscriptions = [];

    private bool _disposed;

    protected bool IsDisposed => _disposed;

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual ValueTask DisposeAsyncCore(bool disposing)
    {
        if (!disposing || _disposed) { return ValueTask.CompletedTask; }

        _disposed = true;

        foreach (var subscription in _subscriptions) { subscription.Dispose(); }

        _subscriptions.Clear();

        return ValueTask.CompletedTask;
    }

    protected void ObserveSource(Action<Action> subscribe, Action<Action> unsubscribe) =>
        _subscriptions.Add(new SourceSubscription(subscribe, unsubscribe, () => InvokeAsync(StateHasChanged)));

    protected void ObserveSource(Action<Action> subscribe, Action<Action> unsubscribe, Func<Task> onChangedAsync) =>
        _subscriptions.Add(new SourceSubscription(subscribe, unsubscribe, () => InvokeAsync(onChangedAsync)));

    protected void ObserveSource(IChangeNotifier source)
    {
        ArgumentNullException.ThrowIfNull(source);

        ObserveSource(handler => source.Changed += handler, handler => source.Changed -= handler);
    }

    protected void ObserveSource(IChangeNotifier source, Func<Task> onChangedAsync)
    {
        ArgumentNullException.ThrowIfNull(source);

        ObserveSource(handler => source.Changed += handler, handler => source.Changed -= handler, onChangedAsync);
    }

    protected void RequestGuardedRender(Action render)
    {
        if (_disposed) { return; }

        _ = DispatchGuardedRenderAsync(render);
    }

    private async Task DispatchGuardedRenderAsync(Action render)
    {
        try
        {
            await InvokeAsync(() =>
            {
                if (_disposed) { return; }

                render();
            });
        }
        catch (ObjectDisposedException) { /* Renderer torn down between the guard and the dispatch; nothing to render. */ }
        catch (OperationCanceledException) { /* Circuit shutting down; nothing to render. */ }
    }
}
