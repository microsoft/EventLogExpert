// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Database;

namespace EventLogExpert.UI.Banner;

public enum BannerView
{
    None,
    Critical,
    Error,
    Attention,
    UpgradeProgress,
    Info
}

public sealed record BannerCycleItem(BannerView View, int IndexWithinSlice, Guid? EntryId);

public static class BannerViewSelector
{
    public static IReadOnlyList<BannerCycleItem> BuildCycle(
        Exception? currentCritical,
        IReadOnlyList<ErrorBannerEntry> errorBanners,
        IReadOnlyList<DatabaseEntry> attentionEntries,
        bool attentionDismissed,
        BannerProgressEntry? backgroundProgress,
        IReadOnlyList<BannerInfoEntry> infoBanners)
    {
        ArgumentNullException.ThrowIfNull(errorBanners);
        ArgumentNullException.ThrowIfNull(attentionEntries);
        ArgumentNullException.ThrowIfNull(infoBanners);

        if (currentCritical is not null)
        {
            return [new BannerCycleItem(BannerView.Critical, 0, null)];
        }

        var items = new List<BannerCycleItem>(
            errorBanners.Count + (attentionEntries.Count > 0 && !attentionDismissed ? 1 : 0)
            + (backgroundProgress is not null ? 1 : 0) + infoBanners.Count);

        for (int i = 0; i < errorBanners.Count; i++)
        {
            items.Add(new BannerCycleItem(BannerView.Error, i, errorBanners[i].Id));
        }

        if (attentionEntries.Count > 0 && !attentionDismissed)
        {
            items.Add(new BannerCycleItem(BannerView.Attention, 0, null));
        }

        if (backgroundProgress is not null)
        {
            items.Add(new BannerCycleItem(BannerView.UpgradeProgress, 0, null));
        }

        for (int i = 0; i < infoBanners.Count; i++)
        {
            items.Add(new BannerCycleItem(BannerView.Info, i, infoBanners[i].Id));
        }

        return items;
    }
}
