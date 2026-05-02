// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Services;

public enum BannerView
{
    None,
    Critical,
    Error,
    Attention,
    UpgradeProgress,
    Info
}

/// <summary>
///     One step in the BannerHost's flat cycle. <see cref="View" /> identifies the rendered card type,
///     <see cref="IndexWithinSlice" /> identifies which entry within a multi-entry slice (Error/Info) is shown,
///     and <see cref="EntryId" /> carries the underlying entry's stable identifier for Error/Info items so the
///     host can preserve a user's selection across rebuilds even when preceding entries are dismissed (which
///     would otherwise shift <see cref="IndexWithinSlice" /> beneath them). Singleton slices (Critical,
///     Attention, UpgradeProgress) carry <c>IndexWithinSlice = 0</c> and <c>EntryId = null</c> because the
///     <see cref="View" /> alone is a stable identity for them.
/// </summary>
public sealed record BannerCycleItem(BannerView View, int IndexWithinSlice, Guid? EntryId);

public static class BannerViewSelector
{
    /// <summary>
    ///     Builds the flat ordered list of items the user can cycle through in BannerHost. Each error and each info
    ///     contributes its own item; Attention and UpgradeProgress contribute one item each when active. Critical
    ///     LOCKS the cycle to a single-item list so the user cannot dismiss-by-cycling past an unresolved critical;
    ///     other slices never combine with Critical.
    /// </summary>
    /// <remarks>
    ///     Order within the cycle: errors -&gt; attention -&gt; upgrade-progress -&gt; infos. Critical, when present,
    ///     pre-empts the entire list. The order is stable so the BannerHost can preserve the user's selection across
    ///     rebuilds by matching the <see cref="BannerCycleItem.View" />/<see cref="BannerCycleItem.EntryId" /> pair
    ///     (record equality is intentionally NOT used for that match because <see cref="BannerCycleItem.IndexWithinSlice" />
    ///     legitimately shifts when preceding entries are dismissed).
    /// </remarks>
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
