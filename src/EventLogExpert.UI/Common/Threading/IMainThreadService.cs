// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Common.Threading;

public interface IMainThreadService
{
    Task InvokeOnMainThread(Action action);

    /// <summary>Invoke an asynchronous delegate on the main thread and await it.</summary>
    Task InvokeOnMainThreadAsync(Func<Task> action);
}
