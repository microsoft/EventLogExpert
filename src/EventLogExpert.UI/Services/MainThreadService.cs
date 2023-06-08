// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Services;

public interface IMainThreadService
{
    Task InvokeOnMainThread(Action action);
}

public class MainThreadService : IMainThreadService
{
    private readonly Func<Action, Task> _mainThreadInvoker;

    public MainThreadService(Func<Action, Task> mainThreadInvoker)
    {
        _mainThreadInvoker = mainThreadInvoker;
    }

    public async Task InvokeOnMainThread(Action action)
    {
        await _mainThreadInvoker(action);
    }
}
