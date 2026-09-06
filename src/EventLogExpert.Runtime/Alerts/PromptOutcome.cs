// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Alerts;

public enum PromptChoice
{
    Cancel,
    Primary,
    Secondary,
}

public readonly record struct PromptOutcome(PromptChoice Choice, string Value);
