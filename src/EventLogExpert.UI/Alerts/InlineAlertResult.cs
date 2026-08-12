// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Alerts;

public sealed record InlineAlertResult(bool Accepted, string? PromptValue)
{
    public bool SecondaryChosen { get; init; }
}
