// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Banner;

public interface IInfoBannerService
{
    event Action StateChanged;

    IReadOnlyList<BannerInfoEntry> InfoBanners { get; }

    void DismissInfoBanner(BannerId id);

    void ReportInfoBanner(string title, string message, BannerSeverity severity);
}
