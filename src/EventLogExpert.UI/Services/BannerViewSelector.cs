// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Services;

public enum BannerView
{
    None,
    Error,
    Critical,
    Info
}

public static class BannerViewSelector
{
    public static BannerView Select(
        Exception? unhandledError,
        IReadOnlyList<CriticalAlertEntry> criticalAlerts,
        IReadOnlyList<BannerInfoEntry> infoBanners)
    {
        if (unhandledError is not null) { return BannerView.Error; }

        if (criticalAlerts.Count > 0) { return BannerView.Critical; }

        return infoBanners.Count > 0 ? BannerView.Info : BannerView.None;
    }
}
