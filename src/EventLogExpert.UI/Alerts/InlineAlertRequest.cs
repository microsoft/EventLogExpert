// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Alerts;

/// <summary>
///     Describes an alert/prompt hosted inline by an active modal. <c>AcceptLabel</c> null means dismiss-only;
///     <c>IsPrompt</c> true renders an input field below the message.
/// </summary>
public sealed record InlineAlertRequest(
    string Title,
    string Message,
    string? AcceptLabel,
    string CancelLabel,
    bool IsPrompt,
    string? PromptInitialValue);
