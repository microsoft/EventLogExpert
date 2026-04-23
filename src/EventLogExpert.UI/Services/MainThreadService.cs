// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Services;

public interface IMainThreadService
{
    Task InvokeOnMainThread(Action action);

    /// <summary>Invoke an asynchronous delegate on the main thread and await it.</summary>
    Task InvokeOnMainThreadAsync(Func<Task> action);
}

public class MainThreadService(
    Func<Action, Task> mainThreadInvoker,
    Func<Func<Task>, Task>? mainThreadAsyncInvoker = null) : IMainThreadService
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

        // Fallback: marshal via the sync overload, capturing the inner Task so exceptions and
        // completion propagate.
        Task? inner = null;
        await _mainThreadInvoker(() => { inner = action(); });

        if (inner is not null)
        {
            await inner;
        }
    }
}
