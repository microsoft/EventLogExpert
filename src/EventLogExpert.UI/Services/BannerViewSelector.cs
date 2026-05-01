// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Services;

public enum BannerView
{
    None,
    Critical,
    Error,
    Info
}

public static class BannerViewSelector
{
    public static BannerView Select(
        Exception? currentCritical,
        IReadOnlyList<ErrorBannerEntry> errorBanners,
        IReadOnlyList<BannerInfoEntry> infoBanners)
    {
        if (currentCritical is not null) { return BannerView.Critical; }

        if (errorBanners.Count > 0) { return BannerView.Error; }

        return infoBanners.Count > 0 ? BannerView.Info : BannerView.None;
    }
}
