// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.Database.Upgrade;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.Banner;

/// <summary>
///     Verifies the "shared backing store, 5 separate StateChanged events" invariant that PR 6 ships. Constructs a
///     REAL <see cref="BannerService" /> with substituted <see cref="IDatabaseService" /> + <see cref="ITraceLogger" />;
///     counts per-facet StateChanged fires; asserts each mutation raises exactly the right facet (and only that facet).
///     Protects the explicit-interface event implementation from a future regression where a single field-like event would
///     silently collapse all 5 invocation lists into one shared multicast.
/// </summary>
public sealed class BannerServiceFacetCoherenceTests
{
    [Fact]
    public void AllFiveFacetInterfaces_ResolveToSameBannerServiceInstance()
    {
        // Arrange
        var databaseService = Substitute.For<IDatabaseService>();
        databaseService.Entries.Returns([]);
        var sut = new BannerService(databaseService, Substitute.For<ITraceLogger>());

        // Act
        IAttentionBannerService attention = sut;
        IProgressBannerService progress = sut;
        ICriticalErrorService critical = sut;
        IErrorBannerService error = sut;
        IInfoBannerService info = sut;

        // Assert — interface-typed references all point to the same backing instance.
        Assert.Same(sut, attention);
        Assert.Same(sut, progress);
        Assert.Same(sut, critical);
        Assert.Same(sut, error);
        Assert.Same(sut, info);
    }

    [Fact]
    public void DismissAttention_FiresOnlyAttentionStateChanged()
    {
        // Arrange — non-empty attention set so DismissAttention is non-no-op.
        var counters = new FacetCounters();
        var databaseService = Substitute.For<IDatabaseService>();
        databaseService.Entries.Returns([BuildAttentionEntry("a.db")]);
        var sut = new BannerService(databaseService, Substitute.For<ITraceLogger>());
        counters.AttachAll(sut);

        // Act
        sut.DismissAttention();

        // Assert
        counters.AssertOnly(nameof(IAttentionBannerService));
    }

    [Fact]
    public void OnEntriesChanged_BothEntriesAndDismissedChange_FiresAttentionExactlyOnce()
    {
        // Arrange — pre-dismiss the attention banner against entry set { a.db }; raising EntriesChanged
        // with NEW entries { b.db } must (a) update _attentionEntries AND (b) un-dismiss because b.db is
        // not in the previously-dismissed file-name set — both inside the same lock. The contract is
        // EXACTLY ONE Attention.StateChanged fire even though two pieces of attention state mutated.
        var databaseService = Substitute.For<IDatabaseService>();
        databaseService.Entries.Returns([BuildAttentionEntry("a.db")]);
        var sut = new BannerService(databaseService, Substitute.For<ITraceLogger>());
        sut.DismissAttention();

        var counters = new FacetCounters();
        counters.AttachAll(sut);

        // Act — replace entries with a different file-name; OnEntriesChanged sees fresh entries + flips dismissed.
        databaseService.Entries.Returns([BuildAttentionEntry("b.db")]);
        databaseService.EntriesChanged += Raise.Event<EventHandler>(databaseService, EventArgs.Empty);

        // Assert — Attention fired exactly once; no other facet fired.
        Assert.Equal(1, counters.AttentionCount);
        counters.AssertOnly(nameof(IAttentionBannerService));
    }

    [Fact]
    public void OnUpgradeBatchStarted_FiresOnlyProgressStateChanged()
    {
        // Arrange
        var counters = new FacetCounters();
        var databaseService = Substitute.For<IDatabaseService>();
        databaseService.Entries.Returns(Array.Empty<DatabaseEntry>());
        var sut = new BannerService(databaseService, Substitute.For<ITraceLogger>());
        counters.AttachAll(sut);

        // Act
        using var cts = new CancellationTokenSource();
        databaseService.UpgradeBatchStarted += Raise.EventWith(
            databaseService,
            new UpgradeBatchStartedEventArgs(UpgradeBatchId.Create(), UpgradeProgressScope.Background, 1, cts));

        // Assert
        counters.AssertOnly(nameof(IProgressBannerService));
    }

    [Fact]
    public void ReportCritical_FiresOnlyCriticalStateChanged()
    {
        // Arrange
        var counters = new FacetCounters();
        var databaseService = Substitute.For<IDatabaseService>();
        databaseService.Entries.Returns(Array.Empty<DatabaseEntry>());
        var sut = new BannerService(databaseService, Substitute.For<ITraceLogger>());
        counters.AttachAll(sut);

        // Act
        sut.ReportCritical(new InvalidOperationException("boom"));

        // Assert
        counters.AssertOnly(nameof(ICriticalErrorService));
    }

    [Fact]
    public void ReportError_FiresOnlyErrorStateChanged()
    {
        // Arrange
        var counters = new FacetCounters();
        var databaseService = Substitute.For<IDatabaseService>();
        databaseService.Entries.Returns(Array.Empty<DatabaseEntry>());
        var sut = new BannerService(databaseService, Substitute.For<ITraceLogger>());
        counters.AttachAll(sut);

        // Act
        sut.ReportError("title", "msg");

        // Assert
        counters.AssertOnly(nameof(IErrorBannerService));
    }

    [Fact]
    public void ReportError_VisibleOnErrorFacet_DoesNotMutateAttentionEntries()
    {
        // Arrange
        var databaseService = Substitute.For<IDatabaseService>();
        databaseService.Entries.Returns(Array.Empty<DatabaseEntry>());
        var sut = new BannerService(databaseService, Substitute.For<ITraceLogger>());
        int initialAttentionCount = sut.AttentionEntries.Count;

        // Act
        BannerId id = sut.ReportError("title", "msg");

        // Assert — Error facet observed the mutation; Attention facet's state was unaffected.
        Assert.Single(sut.ErrorBanners);
        Assert.Equal(id, sut.ErrorBanners[0].Id);
        Assert.Equal(initialAttentionCount, sut.AttentionEntries.Count);
    }

    [Fact]
    public void ReportInfoBanner_FiresOnlyInfoStateChanged()
    {
        // Arrange
        var counters = new FacetCounters();
        var databaseService = Substitute.For<IDatabaseService>();
        databaseService.Entries.Returns(Array.Empty<DatabaseEntry>());
        var sut = new BannerService(databaseService, Substitute.For<ITraceLogger>());
        counters.AttachAll(sut);

        // Act
        sut.ReportInfoBanner("title", "msg", BannerSeverity.Info);

        // Assert
        counters.AssertOnly(nameof(IInfoBannerService));
    }

    private static DatabaseEntry BuildAttentionEntry(string fileName) =>
        new(fileName, $@"c:\dbs\{fileName}", true, DatabaseStatus.UpgradeRequired);

    private sealed class FacetCounters
    {
        public int AttentionCount { get; private set; }

        public int CriticalCount { get; private set; }

        public int ErrorCount { get; private set; }

        public int InfoCount { get; private set; }

        public int ProgressCount { get; private set; }

        public void AssertOnly(string facetName, int expectedCount = 1)
        {
            Assert.Equal(facetName == nameof(IAttentionBannerService) ? expectedCount : 0, AttentionCount);
            Assert.Equal(facetName == nameof(IProgressBannerService) ? expectedCount : 0, ProgressCount);
            Assert.Equal(facetName == nameof(ICriticalErrorService) ? expectedCount : 0, CriticalCount);
            Assert.Equal(facetName == nameof(IErrorBannerService) ? expectedCount : 0, ErrorCount);
            Assert.Equal(facetName == nameof(IInfoBannerService) ? expectedCount : 0, InfoCount);
        }

        public void AttachAll(BannerService sut)
        {
            ((IAttentionBannerService)sut).StateChanged += () => AttentionCount++;
            ((IProgressBannerService)sut).StateChanged += () => ProgressCount++;
            ((ICriticalErrorService)sut).StateChanged += () => CriticalCount++;
            ((IErrorBannerService)sut).StateChanged += () => ErrorCount++;
            ((IInfoBannerService)sut).StateChanged += () => InfoCount++;
        }
    }
}
