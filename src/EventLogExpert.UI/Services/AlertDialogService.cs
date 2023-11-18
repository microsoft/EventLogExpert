// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Services;

public interface IAlertDialogService
{
    Task ShowAlert(string title, string message, string cancel);

    Task<bool> ShowAlert(string title, string message, string accept, string cancel);
}

public sealed class AlertDialogService(
    Func<string, string, string, Task> oneButtonAlert,
    Func<string, string, string, string, Task<bool>> twoButtonAlert) : IAlertDialogService
{
    public Task ShowAlert(string title, string message, string cancel) => oneButtonAlert(title, message, cancel);

    public Task<bool> ShowAlert(string title, string message, string accept, string cancel) =>
        twoButtonAlert(title, message, accept, cancel);
}
