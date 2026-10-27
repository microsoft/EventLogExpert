// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Banner;

public sealed record NotificationDismissSnapshot(
    ImmutableArray<ErrorBannerEntry> Errors,
    ImmutableArray<BannerInfoEntry> Infos,
    long? AttentionVersion)
{
    public int Count => Errors.Length + Infos.Length + (AttentionVersion.HasValue ? 1 : 0);
}
