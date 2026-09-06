// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Banner;

public sealed record ErrorBannerEntry(
    BannerId Id,
    BannerMessage Content,
    Func<Task>? Action,
    DateTime CreatedUtc);
