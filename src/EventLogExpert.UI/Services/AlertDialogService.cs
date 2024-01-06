// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Services;

public interface IAlertDialogService
{
    Task ShowAlert(string title, string message, string cancel);

    Task<bool> ShowAlert(string title, string message, string accept, string cancel);

    Task<string> DisplayPrompt(string title, string mesage);
}

public sealed class AlertDialogService(
    Func<string, string, string, Task> oneButtonAlert,
    Func<string, string, string, string, Task<bool>> twoButtonAlert,
    Func<string, string, Task<string>> promptAlert) : IAlertDialogService
{
    public async Task ShowAlert(string title, string message, string cancel) =>
        await oneButtonAlert(title, message, cancel);

    public async Task<bool> ShowAlert(string title, string message, string accept, string cancel) =>
        await twoButtonAlert(title, message, accept, cancel);

    public async Task<string> DisplayPrompt(string title, string message) => await promptAlert(title, message);
}
