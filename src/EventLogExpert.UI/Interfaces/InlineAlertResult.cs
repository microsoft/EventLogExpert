// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Interfaces;

/// <summary>Result of an inline alert. <see cref="PromptValue" /> is non-null only for prompt requests.</summary>
public sealed record InlineAlertResult(bool Accepted, string? PromptValue);
