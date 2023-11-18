// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Services;

public interface IMainThreadService
{
    Task InvokeOnMainThread(Action action);
}

public sealed class MainThreadService(Func<Action, Task> mainThreadInvoker) : IMainThreadService
{
    public Task InvokeOnMainThread(Action action) => mainThreadInvoker(action);
}
