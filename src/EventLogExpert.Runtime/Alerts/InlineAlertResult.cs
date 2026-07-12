// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Alerts;

/// <summary>Result of an inline alert. <see cref="PromptValue" /> is non-null only for prompt requests.</summary>
public sealed record InlineAlertResult(bool Accepted, string? PromptValue)
{
    /// <summary>
    ///     True when the user clicked the optional <see cref="InlineAlertRequest.SecondaryActionLabel" /> button.
    ///     Mutually exclusive with <see cref="Accepted" />; both false means the alert was cancelled/dismissed.
    /// </summary>
    public bool SecondaryChosen { get; init; }
}
