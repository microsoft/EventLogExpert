// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Services;

/// <summary>
/// Describes an alert/prompt that an active modal can host inline (rendered as a banner inside
/// the host modal's chrome) instead of opening a new modal.
/// </summary>
/// <param name="Title">Banner title. May be empty for message-only alerts.</param>
/// <param name="Message">Body text shown under the title.</param>
/// <param name="AcceptLabel">
/// Label for the accept button. When <c>null</c> the alert is single-button (cancel only).
/// </param>
/// <param name="CancelLabel">Label for the cancel/dismiss button.</param>
/// <param name="IsPrompt">When <c>true</c>, an input field is rendered below the message.</param>
/// <param name="PromptInitialValue">Initial value for the prompt input (ignored when not a prompt).</param>
public sealed record InlineAlertRequest(
    string Title,
    string Message,
    string? AcceptLabel,
    string CancelLabel,
    bool IsPrompt,
    string? PromptInitialValue);

/// <summary>
/// Result of an inline alert. <see cref="Accepted"/> is <c>true</c> when the user pressed the
/// accept/OK button, <c>false</c> when the user pressed cancel or dismissed via Esc.
/// <see cref="PromptValue"/> carries the input value for prompt requests; otherwise <c>null</c>.
/// </summary>
public sealed record InlineAlertResult(bool Accepted, string? PromptValue);

/// <summary>
/// Implemented by an active modal so the alert dialog service can route alert requests as inline
/// banners instead of opening a separate alert modal (which would cancel the active one).
/// </summary>
public interface IInlineAlertHost
{
    /// <summary>
    /// Show <paramref name="request"/> inline on this host. Replaces any prior pending inline
    /// alert (its task is canceled). The returned task completes when the user resolves the alert
    /// or the host is torn down.
    /// </summary>
    Task<InlineAlertResult> ShowInlineAlertAsync(InlineAlertRequest request, CancellationToken cancellationToken);
}
