// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Banner;

public interface IInfoBannerService
{
    event Action StateChanged;

    IReadOnlyList<BannerInfoEntry> InfoBanners { get; }

    void DismissInfoBanner(BannerId id);

    void ReportInfoBanner(BannerMessage content, BannerSeverity severity);
}
