// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Alerts;

public sealed record InlineAlertRequest(
    string Title,
    string Message,
    string? AcceptLabel,
    string CancelLabel,
    bool IsPrompt,
    string? PromptInitialValue)
{
    public Func<string, string?>? Validate { get; init; }

    /// <summary>
    ///     Optional third button rendered between Accept and Cancel. When the user clicks it the result is
    ///     <see cref="InlineAlertResult.SecondaryChosen" /> = true (Accepted stays false). Escape/Cancel remain a plain
    ///     dismissal (both flags false), so a secondary action is never triggered by dismissing the alert.
    /// </summary>
    public string? SecondaryActionLabel { get; init; }
}
