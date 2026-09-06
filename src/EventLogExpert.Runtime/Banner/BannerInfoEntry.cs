// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Banner;

public sealed record BannerInfoEntry(
    BannerId Id,
    BannerMessage Content,
    BannerSeverity Severity,
    DateTime CreatedUtc);
