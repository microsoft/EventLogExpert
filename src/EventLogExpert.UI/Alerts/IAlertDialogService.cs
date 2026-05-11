// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Alerts;

public interface IAlertDialogService
{
    Task<string> DisplayPrompt(string title, string message);

    Task<string> DisplayPrompt(string title, string message, string initialValue);

    Task ShowAlert(string title, string message, string cancel);

    Task ShowAlert(string title, string message, string cancel, AlertPresentation presentation);

    Task<bool> ShowAlert(string title, string message, string accept, string cancel);

    Task<bool> ShowAlert(string title, string message, string accept, string cancel, AlertPresentation presentation);

    Task ShowErrorAlert(string title, string message, string? actionLabel = null, Func<Task>? action = null);
}
