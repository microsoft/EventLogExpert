// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Interfaces;

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

/// <summary>Result of an inline alert. <see cref="PromptValue" /> is non-null only for prompt requests.</summary>
public sealed record InlineAlertResult(bool Accepted, string? PromptValue);

/// <summary>
///     Implemented by an active modal so alerts can be routed as inline banners instead of opening a separate alert
///     modal (which would cancel the active one).
/// </summary>
public interface IInlineAlertHost
{
    /// <summary>Show <paramref name="request" /> inline. Replaces any prior pending inline alert (its task is canceled).</summary>
    Task<InlineAlertResult> ShowInlineAlertAsync(InlineAlertRequest request, CancellationToken cancellationToken);
}
