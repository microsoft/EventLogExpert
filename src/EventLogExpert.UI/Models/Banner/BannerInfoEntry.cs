// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Models;

public sealed record BannerInfoEntry(
    Guid Id,
    string Title,
    string Message,
    BannerSeverity Severity,
    DateTime CreatedUtc);
