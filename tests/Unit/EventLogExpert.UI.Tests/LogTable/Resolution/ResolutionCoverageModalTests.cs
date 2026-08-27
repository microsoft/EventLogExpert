// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.ResolutionCoverage;
using EventLogExpert.UI.LogTable.Resolution;
using EventLogExpert.UI.Modal;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.LogTable.Resolution;

public sealed class ResolutionCoverageModalTests : BunitContext
{
    private static readonly TimeSpan s_wait = TimeSpan.FromSeconds(10);

    private readonly IClipboardService _clipboard = Substitute.For<IClipboardService>();
    private readonly IResolutionCoverageService _coverageService = Substitute.For<IResolutionCoverageService>();
    private readonly IFilterAppliedSource _filterApplied = Substitute.For<IFilterAppliedSource>();
    private readonly IFilterLensCommands _lensCommands = Substitute.For<IFilterLensCommands>();
    private readonly IModalCoordinator _modalCoordinator = Substitute.For<IModalCoordinator>();
    private readonly ModalId _modalId = new(1L);
    private readonly IModalService _modalService = Substitute.For<IModalService>();
    private readonly IEventColumnView _view = Substitute.For<IEventColumnView>();
    private readonly IOrderedViewSource _viewSource = Substitute.For<IOrderedViewSource>();

    private OrderedViewPresentation _presentation;

    public ResolutionCoverageModalTests()
    {
        Services.AddBannerHostDependencies();
        Services.AddMenuMocks();

        _modalService.ActiveModalId.Returns(_modalId);

        _presentation = PresentationWith(PresentationState.Current);
        _viewSource.Current.Returns(_ => _presentation);

        // ResolutionCoverageModal offloads its report/detail builds via Task.Run; bUnit's synchronous .Click() does not
        // await that thread-pool continuation, so an assertion right after a click can race a still-settling re-render and
        // observe a dropped click (reproduced with the async scheduler in isolation, so it is inherent to this file, not a
        // cross-test effect). Running inline completes the build within the render/click dispatch, removing the race.
        // Trade-off: the _loading/_detailLoading render pass is collapsed and a stray ConfigureAwait(false) on the
        // report/detail continuation is unobservable here (see ResolutionCoverageModal.razor.cs) - neither is asserted here.
        Services.AddInlineCpuWorkScheduler();
        Services.AddSingleton(_clipboard);
        Services.AddSingleton(_coverageService);
        Services.AddSingleton(_filterApplied);
        Services.AddSingleton(_lensCommands);
        Services.AddSingleton(_modalCoordinator);
        Services.AddSingleton(_modalService);
        Services.AddSingleton(_viewSource);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task CauseFilter_ClickingNoProvider_AppliesResolutionStatusLens()
    {
        SetReport(Report(Row("A", total: 5, noProvider: 5, status: CoverageStatus.None)));

        var cut = Render<ResolutionCoverageModal>();
        cut.WaitForState(() => cut.FindAll(".resolution-cause-filter").Count > 0, s_wait);

        // Only the non-zero cause (No provider metadata) renders as a button.
        await cut.Find(".resolution-cause-filter").ClickAsync(new MouseEventArgs());

        _lensCommands.Received(1).IncludeValue(EventProperty.ResolutionStatus, ResolutionStatusTokens.NoProvider, Arg.Any<string?>());
    }

    [Fact]
    public void ClickingProviderHeader_SortsByProviderAscending()
    {
        SetReport(Report(
            Row("Zeta", total: 5, noProvider: 5, status: CoverageStatus.None),
            Row("Alpha", total: 3, noProvider: 3, status: CoverageStatus.None)));

        var cut = Render<ResolutionCoverageModal>();
        cut.WaitForState(() => cut.FindAll(".resolution-coverage-row").Count == 2, s_wait);

        Assert.Equal("Zeta", cut.FindAll(".resolution-coverage-row")[0].QuerySelector(".resolution-provider-name")!.TextContent);

        cut.FindAll(".resolution-sort")[0].Click();

        Assert.Equal("Alpha", cut.FindAll(".resolution-coverage-row")[0].QuerySelector(".resolution-provider-name")!.TextContent);
    }

    [Fact]
    public async Task CopyButton_CopiesTableToClipboard()
    {
        SetReport(Report(Row("Alpha", total: 5, noProvider: 5, status: CoverageStatus.None)));

        var cut = Render<ResolutionCoverageModal>();
        cut.WaitForState(() => cut.FindAll(".resolution-coverage-copy").Count > 0, s_wait);

        await cut.Find(".resolution-coverage-copy").ClickAsync(new MouseEventArgs());

        await _clipboard.Received(1).CopyTextAsync(Arg.Is<string>(text => text != null && text.Contains("Alpha", StringComparison.Ordinal)));
    }

    [Fact]
    public void DatabaseAction_ShownForMetadataGaps_HiddenForFailedOnly()
    {
        SetReport(Report(
            Row("NoMeta", total: 5, noProvider: 5, status: CoverageStatus.None),
            Row("Erroring", total: 3, failed: 3, status: CoverageStatus.Partial)));

        var cut = Render<ResolutionCoverageModal>();
        cut.WaitForState(() => cut.FindAll(".resolution-coverage-row").Count == 2, s_wait);

        var rows = cut.FindAll(".resolution-coverage-row");
        Assert.Single(rows[0].QuerySelectorAll(".resolution-dbtools"));
        Assert.Empty(rows[1].QuerySelectorAll(".resolution-dbtools"));
    }

    [Fact]
    public void EmptyReport_Filtered_SaysNoMatch()
    {
        _filterApplied.IsFilteringEnabled.Returns(true);
        SetReport(Report());

        var cut = Render<ResolutionCoverageModal>();

        cut.WaitForAssertion(
            () => Assert.Contains("No events match the current filter", cut.Find(".resolution-coverage-status").TextContent),
            s_wait);
    }

    [Fact]
    public void EmptyReport_Unfiltered_SaysNoEvents()
    {
        _filterApplied.IsFilteringEnabled.Returns(false);
        SetReport(Report());

        var cut = Render<ResolutionCoverageModal>();

        cut.WaitForAssertion(
            () => Assert.Contains("No events to analyze", cut.Find(".resolution-coverage-status").TextContent),
            s_wait);
    }

    [Fact]
    public void ExpandProvider_LoadsAndShowsEventIdBreakdown()
    {
        SetReport(Report(Row("A", total: 5, noProvider: 5, status: CoverageStatus.None)));
        SetProviderDetail(Detail(eventIds: [IdRow(4624, total: 3, noProvider: 3)], distinctUnresolved: 1));

        var cut = Render<ResolutionCoverageModal>();
        cut.WaitForState(() => cut.FindAll(".resolution-provider-toggle").Count > 0, s_wait);

        cut.Find(".resolution-provider-toggle").Click();

        cut.WaitForAssertion(() => Assert.Contains("4624", cut.Find(".resolution-detail-idtable").TextContent), s_wait);
    }

    [Fact]
    public async Task ExpandedEventIdLens_AppliesProviderEventIdUnresolvedLenses()
    {
        SetReport(Report(Row("A", total: 5, noProvider: 5, status: CoverageStatus.None)));
        SetProviderDetail(Detail(eventIds: [IdRow(4624, total: 3, noProvider: 3)], distinctUnresolved: 1));

        var cut = Render<ResolutionCoverageModal>();
        cut.WaitForState(() => cut.FindAll(".resolution-provider-toggle").Count > 0, s_wait);
        cut.Find(".resolution-provider-toggle").Click();
        cut.WaitForState(() => cut.FindAll(".resolution-detail-idtable .resolution-lens").Count > 0, s_wait);

        await cut.Find(".resolution-detail-idtable .resolution-lens").ClickAsync(new MouseEventArgs());

        _lensCommands.Received(1).IncludeValue(EventProperty.Source, "A", Arg.Any<string?>());
        _lensCommands.Received(1).IncludeEventId(4624, Arg.Any<string?>());
        _lensCommands.Received(1).ExcludeValue(EventProperty.ResolutionStatus, ResolutionStatusTokens.Resolved, Arg.Any<string?>());
    }

    [Fact]
    public void ExpandedEventIds_ShowTruncationNote_WhenCapped()
    {
        SetReport(Report(Row("A", total: 5, noProvider: 5, status: CoverageStatus.None)));
        SetProviderDetail(Detail(eventIds: [IdRow(1, total: 1, noProvider: 1)], distinctUnresolved: 50));

        var cut = Render<ResolutionCoverageModal>();
        cut.WaitForState(() => cut.FindAll(".resolution-provider-toggle").Count > 0, s_wait);

        cut.Find(".resolution-provider-toggle").Click();

        cut.WaitForAssertion(() => Assert.Contains("of 50", cut.Find(".resolution-detail-note").TextContent), s_wait);
    }

    [Fact]
    public void FaultedView_ShowsUnavailableMessage()
    {
        _presentation = new OrderedViewPresentation(_view, EventLogId.Create(), default, PresentationState.Faulted, 1, "boom")
        {
            ActiveLogName = "Live"
        };

        var cut = Render<ResolutionCoverageModal>();

        cut.WaitForAssertion(
            () => Assert.Contains("Coverage is unavailable", cut.Find(".resolution-coverage-status").TextContent),
            s_wait);
    }

    [Fact]
    public void FilteredView_ShowsBadge()
    {
        _filterApplied.IsFilteringEnabled.Returns(true);
        SetReport(Report(Row("A", total: 5, noProvider: 5, status: CoverageStatus.None)));

        var cut = Render<ResolutionCoverageModal>();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".resolution-coverage-badge")), s_wait);
    }

    [Fact]
    public void FooterActions_HiddenWhenReportEmpty()
    {
        // The footer action fragment lives outside the body's _report render chain; the HasReport gate must keep the
        // Copy / Show-unresolved buttons out of the loading/empty/fault states so they are never dead controls.
        SetReport(Report());

        var cut = Render<ResolutionCoverageModal>();
        cut.WaitForAssertion(
            () => Assert.Contains("No events to analyze", cut.Find(".resolution-coverage-status").TextContent),
            s_wait);

        Assert.Empty(cut.FindAll(".resolution-coverage-copy"));
        Assert.Empty(cut.FindAll(".resolution-coverage-filter"));
    }

    [Fact]
    public void FullProvider_HasNoDisclosureToggle()
    {
        SetReport(Report(Row("Full", total: 5, resolved: 5, status: CoverageStatus.Full)));

        var cut = Render<ResolutionCoverageModal>();
        cut.WaitForState(() => cut.FindAll(".resolution-coverage-row").Count > 0, s_wait);

        Assert.Empty(cut.FindAll(".resolution-provider-toggle"));
    }

    [Fact]
    public async Task LensAction_FiltersToProviderUnresolvedEvents()
    {
        SetReport(Report(Row("Alpha", total: 5, noProvider: 5, status: CoverageStatus.None)));

        var cut = Render<ResolutionCoverageModal>();
        cut.WaitForState(() => cut.FindAll(".resolution-lens").Count > 0, s_wait);

        await cut.Find(".resolution-lens").ClickAsync(new MouseEventArgs());

        _lensCommands.Received(1).IncludeValue(EventProperty.Source, "Alpha", Arg.Any<string?>());
        _lensCommands.Received(1).ExcludeValue(EventProperty.ResolutionStatus, ResolutionStatusTokens.Resolved, Arg.Any<string?>());
    }

    [Fact]
    public void LensAction_HiddenForBlankProviderRow()
    {
        // A blank-provider row cannot produce a meaningful provider lens, so the button must not render (its handler
        // would otherwise be a silent no-op).
        SetReport(Report(Row(" ", total: 5, noProvider: 5, status: CoverageStatus.None)));

        var cut = Render<ResolutionCoverageModal>();
        cut.WaitForState(() => cut.FindAll(".resolution-coverage-row").Count > 0, s_wait);

        Assert.Empty(cut.FindAll(".resolution-lens"));
    }

    [Fact]
    public void ProportionBar_RendersOneSegmentPerNonZeroCategory()
    {
        SetReport(Report(Row("A", total: 8, resolved: 5, noProvider: 3, status: CoverageStatus.Partial)));

        var cut = Render<ResolutionCoverageModal>();
        cut.WaitForState(() => cut.FindAll(".resolution-coverage-row").Count > 0, s_wait);

        // Resolved + NoProvider are non-zero; NoMessage + Failed are zero and omitted.
        Assert.Equal(2, cut.FindAll(".resolution-coverage-seg").Count);
    }

    [Fact]
    public void RetryDetail_AfterFailure_RerunsScan_AndRecovers()
    {
        SetReport(Report(Row("A", total: 5, noProvider: 5, status: CoverageStatus.None)));
        int calls = 0;
        _coverageService.BuildProviderDetail(Arg.Any<IEventColumnView>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls++;

                return calls == 1
                    ? throw new InvalidOperationException("boom")
                    : Detail(eventIds: [IdRow(4624, total: 3, noProvider: 3)], distinctUnresolved: 1);
            });

        var cut = Render<ResolutionCoverageModal>();
        cut.WaitForState(() => cut.FindAll(".resolution-provider-toggle").Count > 0, s_wait);
        cut.Find(".resolution-provider-toggle").Click();
        cut.WaitForAssertion(() => Assert.Contains("Could not analyze", cut.Find(".resolution-detail-status").TextContent), s_wait);

        cut.Find(".resolution-detail-status .resolution-cause-filter").Click();

        // Retry re-scans (and here recovers) rather than collapsing: the second scan's Event-ID table appears, which is
        // impossible if Retry had taken ToggleExpandAsync's collapse branch.
        cut.WaitForAssertion(() => Assert.Contains("4624", cut.Find(".resolution-detail-idtable").TextContent), s_wait);
    }

    [Fact]
    public void SortThenCap_SurfacesTrueTopForTheActiveSort()
    {
        var rows = new List<ProviderCoverageRow>();

        for (int i = 0; i < 500; i++)
        {
            rows.Add(Row($"Filler{i:D3}", total: 100, noProvider: 100, status: CoverageStatus.None));
        }

        // Low unresolved (501st by the default order, so hidden) but the largest Events total.
        rows.Add(Row("BigTotal", total: 100000, resolved: 99999, noProvider: 1, status: CoverageStatus.Partial));
        SetReport(Report([.. rows]));

        var cut = Render<ResolutionCoverageModal>();
        cut.WaitForState(() => cut.FindAll(".resolution-coverage-row").Count == 500, s_wait);

        Assert.DoesNotContain(cut.FindAll(".resolution-provider-name"), cell => cell.TextContent == "BigTotal");
        Assert.Contains("Showing the top", cut.Find(".resolution-coverage-truncated").TextContent);

        cut.FindAll(".resolution-sort")[1].Click();

        cut.WaitForAssertion(
            () => Assert.Equal("BigTotal", cut.FindAll(".resolution-coverage-row")[0].QuerySelector(".resolution-provider-name")!.TextContent),
            s_wait);
    }

    [Fact]
    public void Tooltip_ComposesOnlyPresentCauses()
    {
        SetReport(Report(
            Row("Mixed", total: 7, resolved: 1, noProvider: 2, noMessage: 3, failed: 1, status: CoverageStatus.Partial),
            Row("FailedOnly", total: 4, resolved: 1, failed: 3, status: CoverageStatus.Partial)));

        var cut = Render<ResolutionCoverageModal>();
        cut.WaitForState(() => cut.FindAll(".resolution-coverage-row").Count == 2, s_wait);

        var rows = cut.FindAll(".resolution-coverage-row");
        var mixedTip = rows[0].QuerySelector(".coverage-pill")!.GetAttribute("title");
        var failedTip = rows[1].QuerySelector(".coverage-pill")!.GetAttribute("title");

        Assert.Contains("no provider metadata", mixedTip);
        Assert.Contains("no message match", mixedTip);
        Assert.Contains("a resolution error", mixedTip);

        Assert.Contains("a resolution error", failedTip);
        Assert.DoesNotContain("no provider metadata", failedTip);
        Assert.DoesNotContain("no message match", failedTip);
    }

    [Fact]
    public void UnfilteredView_HasNoBadge()
    {
        _filterApplied.IsFilteringEnabled.Returns(false);
        SetReport(Report(Row("A", total: 5, noProvider: 5, status: CoverageStatus.None)));

        var cut = Render<ResolutionCoverageModal>();
        cut.WaitForState(() => cut.FindAll(".resolution-coverage-row").Count > 0, s_wait);

        Assert.Empty(cut.FindAll(".resolution-coverage-badge"));
    }

    [Fact]
    public void UpdatingView_ShowsReopenMessage()
    {
        _presentation = PresentationWith(PresentationState.Updating);

        var cut = Render<ResolutionCoverageModal>();

        cut.WaitForAssertion(
            () => Assert.Contains("still updating", cut.Find(".resolution-coverage-status").TextContent),
            s_wait);
    }

    private static ProviderCoverageDetail Detail(
        IReadOnlyList<EventIdCoverageRow>? eventIds = null,
        IReadOnlyList<LevelCoverageRow>? levels = null,
        int distinctUnresolved = 0) =>
        new(eventIds ?? [], levels ?? [], distinctUnresolved);

    private static EventIdCoverageRow IdRow(int eventId, int total, int noProvider = 0, int noMessage = 0, int failed = 0) =>
        new(eventId, new ProviderResolutionCounts(total, total - noProvider - noMessage - failed, noProvider, noMessage, failed));

    private static ResolutionCoverageReport Report(params ProviderCoverageRow[] rows)
    {
        ProviderResolutionCounts summary = default;

        foreach (var row in rows) { summary = summary.Add(row.Counts); }

        return new ResolutionCoverageReport(summary, rows);
    }

    private static ProviderCoverageRow Row(
        string provider,
        int total,
        int resolved = 0,
        int noProvider = 0,
        int noMessage = 0,
        int failed = 0,
        CoverageStatus status = CoverageStatus.Partial) =>
        new(provider, new ProviderResolutionCounts(total, resolved, noProvider, noMessage, failed), status);

    private OrderedViewPresentation PresentationWith(PresentationState state) =>
        new(_view, EventLogId.Create(), default, state, 1) { ActiveLogName = "Live" };

    private void SetProviderDetail(ProviderCoverageDetail detail) =>
        _coverageService.BuildProviderDetail(Arg.Any<IEventColumnView>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(detail);

    private void SetReport(ResolutionCoverageReport report) =>
        _coverageService.Build(Arg.Any<IEventColumnView>(), Arg.Any<CancellationToken>()).Returns(report);
}
