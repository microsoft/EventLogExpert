// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Banner;
using EventLogExpert.UI.Database;
using EventLogExpert.UI.Database.Upgrade;

namespace EventLogExpert.UI.Tests.Banner;

public sealed class BannerViewSelectorTests
{
    private static readonly DateTime s_testTime = new(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void BuildCycle_AllEmpty_ReturnsEmpty()
    {
        // Act
        IReadOnlyList<BannerCycleItem> result = BannerViewSelector.BuildCycle(
            currentCritical: null,
            errorBanners: [],
            attentionEntries: [],
            attentionDismissed: false,
            backgroundProgress: null,
            infoBanners: []);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void BuildCycle_AllSlicesActive_OrdersErrorsThenAttentionThenUpgradeProgressThenInfos()
    {
        // Arrange
        ErrorBannerEntry e0 = BuildError();
        ErrorBannerEntry e1 = BuildError();
        BannerInfoEntry i0 = BuildInfo();
        BannerInfoEntry i1 = BuildInfo();
        BannerInfoEntry i2 = BuildInfo();

        // Act
        IReadOnlyList<BannerCycleItem> result = BannerViewSelector.BuildCycle(
            currentCritical: null,
            errorBanners: [e0, e1],
            attentionEntries: [BuildAttention("a.db"), BuildAttention("b.db")],
            attentionDismissed: false,
            backgroundProgress: BuildProgress(),
            infoBanners: [i0, i1, i2]);

        // Assert
        Assert.Equal(7, result.Count);
        Assert.Equal(new BannerCycleItem(BannerView.Error, 0, e0.Id), result[0]);
        Assert.Equal(new BannerCycleItem(BannerView.Error, 1, e1.Id), result[1]);
        Assert.Equal(new BannerCycleItem(BannerView.Attention, 0, null), result[2]);
        Assert.Equal(new BannerCycleItem(BannerView.UpgradeProgress, 0, null), result[3]);
        Assert.Equal(new BannerCycleItem(BannerView.Info, 0, i0.Id), result[4]);
        Assert.Equal(new BannerCycleItem(BannerView.Info, 1, i1.Id), result[5]);
        Assert.Equal(new BannerCycleItem(BannerView.Info, 2, i2.Id), result[6]);
    }

    [Fact]
    public void BuildCycle_AttentionDismissed_AttentionExcluded()
    {
        // Act
        IReadOnlyList<BannerCycleItem> result = BannerViewSelector.BuildCycle(
            currentCritical: null,
            errorBanners: [],
            attentionEntries: [BuildAttention("a.db")],
            attentionDismissed: true,
            backgroundProgress: null,
            infoBanners: []);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void BuildCycle_AttentionEntriesEmpty_AttentionExcluded_RegardlessOfDismissedFlag()
    {
        // Act
        IReadOnlyList<BannerCycleItem> result = BannerViewSelector.BuildCycle(
            currentCritical: null,
            errorBanners: [],
            attentionEntries: [],
            attentionDismissed: false,
            backgroundProgress: null,
            infoBanners: []);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void BuildCycle_AttentionEntriesNull_Throws()
    {
        // Act + Assert
        Assert.Throws<ArgumentNullException>(
            () => BannerViewSelector.BuildCycle(
                currentCritical: null,
                errorBanners: [],
                attentionEntries: null!,
                attentionDismissed: false,
                backgroundProgress: null,
                infoBanners: []));
    }

    [Fact]
    public void BuildCycle_CriticalPresent_ReturnsSingleCriticalItem_RegardlessOfOtherSlices()
    {
        // Act
        IReadOnlyList<BannerCycleItem> result = BannerViewSelector.BuildCycle(
            currentCritical: new InvalidOperationException("boom"),
            errorBanners: [BuildError(), BuildError()],
            attentionEntries: [BuildAttention("a.db")],
            attentionDismissed: false,
            backgroundProgress: BuildProgress(),
            infoBanners: [BuildInfo()]);

        // Assert
        Assert.Single(result);
        Assert.Equal(BannerView.Critical, result[0].View);
        Assert.Equal(0, result[0].IndexWithinSlice);
        Assert.Null(result[0].EntryId);
    }

    [Fact]
    public void BuildCycle_ErrorBannersNull_Throws()
    {
        // Act + Assert
        Assert.Throws<ArgumentNullException>(
            () => BannerViewSelector.BuildCycle(
                currentCritical: null,
                errorBanners: null!,
                attentionEntries: [],
                attentionDismissed: false,
                backgroundProgress: null,
                infoBanners: []));
    }

    [Fact]
    public void BuildCycle_InfoBannersNull_Throws()
    {
        // Act + Assert
        Assert.Throws<ArgumentNullException>(
            () => BannerViewSelector.BuildCycle(
                currentCritical: null,
                errorBanners: [],
                attentionEntries: [],
                attentionDismissed: false,
                backgroundProgress: null,
                infoBanners: null!));
    }

    [Fact]
    public void BuildCycle_OnlyAttention_ReturnsSingleAttentionItem()
    {
        // Act
        IReadOnlyList<BannerCycleItem> result = BannerViewSelector.BuildCycle(
            currentCritical: null,
            errorBanners: [],
            attentionEntries: [BuildAttention("a.db"), BuildAttention("b.db")],
            attentionDismissed: false,
            backgroundProgress: null,
            infoBanners: []);

        // Assert
        Assert.Single(result);
        Assert.Equal(new BannerCycleItem(BannerView.Attention, 0, null), result[0]);
    }

    [Fact]
    public void BuildCycle_OnlyErrors_OneItemPerError_StableOrder()
    {
        // Arrange
        ErrorBannerEntry e0 = BuildError();
        ErrorBannerEntry e1 = BuildError();
        ErrorBannerEntry e2 = BuildError();

        // Act
        IReadOnlyList<BannerCycleItem> result = BannerViewSelector.BuildCycle(
            currentCritical: null,
            errorBanners: [e0, e1, e2],
            attentionEntries: [],
            attentionDismissed: false,
            backgroundProgress: null,
            infoBanners: []);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal(new BannerCycleItem(BannerView.Error, 0, e0.Id), result[0]);
        Assert.Equal(new BannerCycleItem(BannerView.Error, 1, e1.Id), result[1]);
        Assert.Equal(new BannerCycleItem(BannerView.Error, 2, e2.Id), result[2]);
    }

    [Fact]
    public void BuildCycle_OnlyInfos_OneItemPerInfo_StableOrder()
    {
        // Arrange
        BannerInfoEntry i0 = BuildInfo();
        BannerInfoEntry i1 = BuildInfo();

        // Act
        IReadOnlyList<BannerCycleItem> result = BannerViewSelector.BuildCycle(
            currentCritical: null,
            errorBanners: [],
            attentionEntries: [],
            attentionDismissed: false,
            backgroundProgress: null,
            infoBanners: [i0, i1]);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal(new BannerCycleItem(BannerView.Info, 0, i0.Id), result[0]);
        Assert.Equal(new BannerCycleItem(BannerView.Info, 1, i1.Id), result[1]);
    }

    [Fact]
    public void BuildCycle_OnlyUpgradeProgress_ReturnsSingleUpgradeProgressItem()
    {
        // Act
        IReadOnlyList<BannerCycleItem> result = BannerViewSelector.BuildCycle(
            currentCritical: null,
            errorBanners: [],
            attentionEntries: [],
            attentionDismissed: false,
            backgroundProgress: BuildProgress(),
            infoBanners: []);

        // Assert
        Assert.Single(result);
        Assert.Equal(new BannerCycleItem(BannerView.UpgradeProgress, 0, null), result[0]);
    }

    [Fact]
    public void BuildCycle_RebuildAfterErrorDismissed_PreservesEntryIdOnSurvivingError()
    {
        // Arrange
        ErrorBannerEntry e0 = BuildError();
        ErrorBannerEntry e1 = BuildError();
        ErrorBannerEntry e2 = BuildError();

        // Act
        IReadOnlyList<BannerCycleItem> before = BannerViewSelector.BuildCycle(
            currentCritical: null,
            errorBanners: [e0, e1, e2],
            attentionEntries: [],
            attentionDismissed: false,
            backgroundProgress: null,
            infoBanners: []);

        IReadOnlyList<BannerCycleItem> after = BannerViewSelector.BuildCycle(
            currentCritical: null,
            errorBanners: [e1, e2],
            attentionEntries: [],
            attentionDismissed: false,
            backgroundProgress: null,
            infoBanners: []);

        // Assert
        Assert.Equal(3, before.Count);
        Assert.Equal(2, after.Count);
        Assert.Equal(new BannerCycleItem(BannerView.Error, 0, e1.Id), after[0]);
        Assert.Equal(new BannerCycleItem(BannerView.Error, 1, e2.Id), after[1]);
    }

    private static DatabaseEntry BuildAttention(string fileName) =>
        new(fileName, $@"C:\dbs\{fileName}", IsEnabled: false, DatabaseStatus.UpgradeRequired);

    private static ErrorBannerEntry BuildError() =>
        new(BannerId.Create(), "Error Title", "Error Message", null, null, s_testTime);

    private static BannerInfoEntry BuildInfo() =>
        new(BannerId.Create(), "Info Title", "Info Message", BannerSeverity.Info, s_testTime);

    private static BannerProgressEntry BuildProgress() =>
        new(
            Guid.NewGuid(),
            UpgradeProgressScope.Background,
            CurrentBatchPosition: 0,
            CurrentBatchSize: 1,
            CurrentEntryName: string.Empty,
            CurrentPhase: UpgradePhase.BackingUp,
            QueuedBatchesAfter: 0,
            Cancel: () => { });
}
