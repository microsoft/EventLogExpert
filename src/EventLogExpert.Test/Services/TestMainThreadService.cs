// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Services;

namespace EventLogExpert.Test.Services;

public class TestMainThreadService : IMainThreadService
{
    public Task InvokeOnMainThread(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}
