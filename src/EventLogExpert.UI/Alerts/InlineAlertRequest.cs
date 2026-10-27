// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Alerts;

public sealed record InlineAlertRequest(
    string Title,
    string Message,
    string? AcceptLabel,
    string CancelLabel,
    bool IsPrompt,
    string? PromptInitialValue)
{
    public bool DefaultToCancel { get; init; }

    public Func<string, string?>? Validate { get; init; }

    public string? SecondaryActionLabel { get; init; }
}
