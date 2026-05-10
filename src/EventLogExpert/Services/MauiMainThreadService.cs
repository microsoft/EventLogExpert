// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Common.Threading;

namespace EventLogExpert.Services;

internal sealed class MauiMainThreadService : IMainThreadService
{
    public Task InvokeOnMainThread(Action action) =>
        InvokeSafely(() => MainThread.InvokeOnMainThreadAsync(action));

    public Task InvokeOnMainThreadAsync(Func<Task> action) =>
        InvokeSafely(() => MainThread.InvokeOnMainThreadAsync(action));

    // Captures synchronous throws from MainThread.InvokeOnMainThreadAsync into a faulted Task so
    // consumers wiring .ContinueWith(OnlyOnFaulted) on the returned Task (e.g. DeploymentService
    // WinRT callbacks) observe failures rather than swallowing an unobserved synchronous throw.
    private static Task InvokeSafely(Func<Task> dispatch)
    {
        try { return dispatch(); }
        catch (Exception ex) { return Task.FromException(ex); }
    }
}
