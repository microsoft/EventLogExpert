// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;

namespace EventLogExpert.UI.Services;

/// <summary>
///     User-facing alert/prompt surface. Implementations decide whether each request renders inline in an active modal,
///     opens a standalone popup, or routes to the singleton banner via <see cref="IBannerService" />.
/// </summary>
public interface IAlertDialogService
{
    Task<string> DisplayPrompt(string title, string message);

    Task<string> DisplayPrompt(string title, string message, string initialValue);

    /// <summary>One-button informational alert. Uses <see cref="AlertPresentation.Auto" /> presentation.</summary>
    Task ShowAlert(string title, string message, string cancel);

    /// <summary>One-button informational alert with explicit presentation control.</summary>
    Task ShowAlert(string title, string message, string cancel, AlertPresentation presentation);

    /// <summary>Two-button confirmation alert. Uses <see cref="AlertPresentation.Auto" /> presentation.</summary>
    Task<bool> ShowAlert(string title, string message, string accept, string cancel);

    /// <summary>
    ///     Two-button confirmation alert with explicit presentation control. <see cref="AlertPresentation.Banner" /> is not
    ///     valid for two-button alerts (the banner has no accept/cancel pair) and throws <see cref="ArgumentException" />.
    /// </summary>
    Task<bool> ShowAlert(string title, string message, string accept, string cancel, AlertPresentation presentation);

    /// <summary>
    ///     Surface a critical alert via <see cref="IBannerService.ReportCritical" />. Always routes to the banner queue
    ///     regardless of whether an inline host is active. The returned task completes immediately after the alert is
    ///     queued; it does NOT wait for the user to dismiss the banner. Caller is responsible for ensuring it is
    ///     appropriate to interrupt the user with a banner.
    /// </summary>
    Task ShowCriticalAlert(string title, string message);
}
