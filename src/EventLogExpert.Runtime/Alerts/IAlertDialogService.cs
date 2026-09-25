// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Runtime.Alerts;

public interface IAlertDialogService
{
    Task<string> DisplayPrompt(string title, string message);

    Task<string> DisplayPrompt(string title, string message, string initialValue);

    Task<string> DisplayPrompt(string title, string message, string initialValue, Func<string, string?>? validate);

    Task<PromptOutcome> DisplayPromptWithSecondary(
        string title,
        string message,
        string initialValue,
        string primaryLabel,
        string secondaryLabel,
        string cancelLabel,
        Func<string, string?>? validate = null);

    Task ShowAlert(string title, string message, string cancel);

    Task ShowAlert(string title, string message, string cancel, AlertPresentation presentation);

    Task<bool> ShowAlert(string title, string message, string accept, string cancel);

    Task<bool> ShowAlert(string title, string message, string accept, string cancel, AlertPresentation presentation);

    /// <summary>
    ///     Shows a one-button alert whose text is supplied as localizable keys; the UI implementation resolves them
    ///     against the shared catalog. Runtime callers use this overload so they stay localizer-free.
    /// </summary>
    Task ShowAlert(LocalizableText title, LocalizableText message, LocalizableText cancel);

    /// <summary>Shows a two-button (accept/cancel) alert whose text is supplied as localizable keys.</summary>
    Task<bool> ShowAlert(LocalizableText title, LocalizableText message, LocalizableText accept, LocalizableText cancel);

    Task ShowErrorAlert(string title, string message, string? actionLabel = null, Func<Task>? action = null);
}
