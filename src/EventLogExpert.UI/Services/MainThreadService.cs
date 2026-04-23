// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Services;

public interface IMainThreadService
{
    Task InvokeOnMainThread(Action action);

    /// <summary>Invoke an asynchronous delegate on the main thread and await it.</summary>
    Task InvokeOnMainThreadAsync(Func<Task> action);
}

public class MainThreadService(Func<Action, Task> mainThreadInvoker, Func<Func<Task>, Task>? mainThreadAsyncInvoker = null) : IMainThreadService
{
    private readonly Func<Func<Task>, Task>? _mainThreadAsyncInvoker = mainThreadAsyncInvoker;
    private readonly Func<Action, Task> _mainThreadInvoker = mainThreadInvoker;

    public async Task InvokeOnMainThread(Action action)
    {
        await _mainThreadInvoker(action);
    }

    public async Task InvokeOnMainThreadAsync(Func<Task> action)
    {
        if (_mainThreadAsyncInvoker is not null)
        {
            await _mainThreadAsyncInvoker(action);
            return;
        }

        // Fallback for tests/contexts that don't supply an async invoker: marshal via the sync
        // overload by capturing the inner Task. We start the work on the main thread but await it
        // here so exceptions and completion propagate correctly.
        Task? inner = null;
        await _mainThreadInvoker(() => { inner = action(); });

        if (inner is not null)
        {
            await inner;
        }
    }
}
