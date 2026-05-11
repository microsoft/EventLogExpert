// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Banner;

public sealed record BannerInfoEntry(
    BannerId Id,
    string Title,
    string Message,
    BannerSeverity Severity,
    DateTime CreatedUtc);
