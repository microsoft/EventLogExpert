// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Banner;

public sealed record ErrorBannerEntry(
    BannerId Id,
    string Title,
    string Message,
    string? ActionLabel,
    Func<Task>? Action,
    DateTime CreatedUtc);
