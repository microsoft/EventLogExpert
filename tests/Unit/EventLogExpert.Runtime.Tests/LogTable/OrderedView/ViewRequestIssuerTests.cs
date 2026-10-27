// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using EventLogExpert.Runtime.Tests.LogTable.TestSupport;
using System.Collections.Immutable;
using System.Reflection;

namespace EventLogExpert.Runtime.Tests.LogTable.OrderedView;

public sealed class ViewRequestIssuerTests
{
    private static readonly TimeSpan s_blockedContenderWindow = TimeSpan.FromMilliseconds(200);

    [Fact]
    public void ArmForce_WhenAForceIsPending_ArmsNoRecoveryEntitlement()
    {
        ViewRequestIssuer issuer = new();
        ViewIdentity identity = IdentityFor(EventLogId.Create());

        Assert.Equal(IssueKind.Issued, issuer.Issue(identity));

        issuer.ArmForce();
        issuer.ArmForce();

        IssueOutcome outcome = issuer.TryIssue(identity, IssuerTestProbe.Matching(identity));

        Assert.Equal(IssueKind.Issued, outcome.Kind);
        Assert.False(outcome.ClearFault);
    }

    [Fact]
    public void ArmForce_WhenARecoveryIsPending_PreservesTheRecoveryEntitlement()
    {
        ViewRequestIssuer issuer = new();
        ViewIdentity identity = IdentityFor(EventLogId.Create());

        Assert.Equal(IssueKind.Issued, issuer.Issue(identity));

        issuer.ArmRecovery();
        issuer.ArmForce();

        IssueOutcome outcome = issuer.TryIssue(identity, IssuerTestProbe.Matching(identity));

        Assert.Equal(IssueKind.Issued, outcome.Kind);
        Assert.True(outcome.ClearFault);
    }

    [Fact]
    public void ArmRecovery_ForcesReissueOfTheUnchangedIdentity_ReportsClearFaultOnce()
    {
        ViewRequestIssuer issuer = new();
        ViewIdentity identity = IdentityFor(EventLogId.Create());

        Assert.Equal(IssueKind.Issued, issuer.Issue(identity));
        Assert.Equal(IssueKind.Deduped, issuer.Issue(identity));

        issuer.ArmRecovery();

        IssueOutcome recovered = issuer.TryIssue(identity, IssuerTestProbe.Matching(identity));

        Assert.Equal(IssueKind.Issued, recovered.Kind);
        Assert.True(recovered.ClearFault);
        Assert.Equal(IssueKind.Deduped, issuer.Issue(identity));

        issuer.ArmForce();

        IssueOutcome forced = issuer.TryIssue(identity, IssuerTestProbe.Matching(identity));

        Assert.Equal(IssueKind.Issued, forced.Kind);
        Assert.False(forced.ClearFault);
    }

    [Fact]
    public void ArmRecovery_WhenAForceIsPending_ArmsTheRecoveryEntitlement()
    {
        ViewRequestIssuer issuer = new();
        ViewIdentity identity = IdentityFor(EventLogId.Create());

        Assert.Equal(IssueKind.Issued, issuer.Issue(identity));

        issuer.ArmForce();
        issuer.ArmRecovery();

        IssueOutcome outcome = issuer.TryIssue(identity, IssuerTestProbe.Matching(identity));

        Assert.Equal(IssueKind.Issued, outcome.Kind);
        Assert.True(outcome.ClearFault);
    }

    [Fact]
    public void ArmRecovery_WhenARecoveryIsPending_PreservesTheRecoveryEntitlement()
    {
        ViewRequestIssuer issuer = new();
        ViewIdentity identity = IdentityFor(EventLogId.Create());

        Assert.Equal(IssueKind.Issued, issuer.Issue(identity));

        issuer.ArmRecovery();
        issuer.ArmRecovery();

        IssueOutcome outcome = issuer.TryIssue(identity, IssuerTestProbe.Matching(identity));

        Assert.Equal(IssueKind.Issued, outcome.Kind);
        Assert.True(outcome.ClearFault);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(8)]
    public void EqualViewIdentities_AcrossDifferingLogCounts_YieldEqualSortContext(int largerLogCount)
    {
        EventLogId activeId = EventLogId.Create();
        LogTableState twoLogs = DefaultOrderedState(activeId, displayedLogCount: 2);
        LogTableState moreLogs = DefaultOrderedState(activeId, largerLogCount);

        Assert.Equal(2, twoLogs.DisplayedLogCount);
        Assert.Equal(largerLogCount, moreLogs.DisplayedLogCount);
        Assert.Equal(ColumnName.DateAndTime, twoLogs.SortContext.OrderBy);
        Assert.Equal(twoLogs.ViewIdentity, moreLogs.ViewIdentity);
        Assert.Equal(twoLogs.SortContext, moreLogs.SortContext);
    }

    [Fact]
    public void ResetForClear_DropsAPendingRecoveryEntitlement()
    {
        ViewRequestIssuer issuer = new();
        ViewIdentity identity = IdentityFor(EventLogId.Create());

        Assert.Equal(IssueKind.Issued, issuer.Issue(identity));

        issuer.ArmRecovery();
        issuer.ResetForClear();

        IssueOutcome outcome = issuer.TryIssue(identity, IssuerTestProbe.Matching(identity));

        Assert.Equal(IssueKind.Issued, outcome.Kind);
        Assert.False(outcome.ClearFault);
    }

    [Fact]
    public void ResetForCloseAll_DropsAPendingRecoveryEntitlement()
    {
        ViewRequestIssuer issuer = new();
        ViewIdentity identity = IdentityFor(EventLogId.Create());

        Assert.Equal(IssueKind.Issued, issuer.Issue(identity));

        issuer.ArmRecovery();
        issuer.ResetForCloseAll();

        IssueOutcome outcome = issuer.TryIssue(identity, IssuerTestProbe.Matching(identity));

        Assert.Equal(IssueKind.Issued, outcome.Kind);
        Assert.False(outcome.ClearFault);
    }

    [Fact]
    public void TryIssue_CarriesTheProbedReadersAndSortContext_IntoTheIssuedOutcome()
    {
        ViewRequestIssuer issuer = new();
        ViewIdentity identity = IdentityFor(EventLogId.Create());
        SortContext sortContext = new(ColumnName.Source, isDescending: true, null, isGroupDescending: false);

        IssueOutcome outcome = issuer.TryIssue(
            identity,
            () => new ViewProbe(identity, IssuerTestProbe.NoReaders, sortContext));

        Assert.Equal(IssueKind.Issued, outcome.Kind);
        Assert.Same(IssuerTestProbe.NoReaders, outcome.Readers);
        Assert.Equal(sortContext, outcome.SortContext);
    }

    [Fact]
    public async Task TryIssue_RunsTheProbeUnderTheGate_SoAConcurrentIssueCannotInterleave()
    {
        ViewRequestIssuer issuer = new();
        ViewIdentity first = IdentityFor(EventLogId.Create());
        ViewIdentity second = IdentityFor(EventLogId.Create());
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        using ManualResetEventSlim probeEntered = new(false);
        using ManualResetEventSlim releaseProbe = new(false);
        using ManualResetEventSlim contenderStarted = new(false);

        Task<IssueOutcome> holder = Task.Run(
            () => issuer.TryIssue(
                first,
                () =>
                {
                    probeEntered.Set();
                    releaseProbe.Wait(cancellationToken);

                    return new ViewProbe(first, IssuerTestProbe.NoReaders, default);
                }),
            cancellationToken);

        Task<IssueOutcome> contender;

        try
        {
            Assert.True(
                probeEntered.Wait(OrderedViewTestTimeouts.Default, cancellationToken),
                "the holder never entered its probe, so there was no held gate to contend for");

            contender = Task.Run(
                () =>
                {
                    contenderStarted.Set();

                    return issuer.TryIssue(second, IssuerTestProbe.Matching(second));
                },
                cancellationToken);

            Assert.True(
                contenderStarted.Wait(OrderedViewTestTimeouts.Default, cancellationToken),
                "the contender never started, so waiting on the gate was never exercised");

            await Task.WhenAny(contender, Task.Delay(s_blockedContenderWindow, cancellationToken));

            Assert.False(
                contender.IsCompleted,
                "a concurrent issue completed while another probe was still running, so the probe is not under the gate");
        }
        finally
        {
            releaseProbe.Set();
        }

        await Task.WhenAll(holder, contender).WaitAsync(OrderedViewTestTimeouts.Default, cancellationToken);

        IssueOutcome holderOutcome = await holder;
        IssueOutcome contenderOutcome = await contender;

        Assert.Equal(IssueKind.Issued, holderOutcome.Kind);
        Assert.Equal(IssueKind.Issued, contenderOutcome.Kind);
        Assert.Equal(holderOutcome.Sequence + 1, contenderOutcome.Sequence);
    }

    [Fact]
    public void TryIssue_StaleAndDeduped_BurnNoSequence()
    {
        ViewRequestIssuer issuer = new();
        ViewIdentity identity = IdentityFor(EventLogId.Create());
        ViewIdentity newer = IdentityFor(EventLogId.Create());

        long firstSequence = issuer.TryIssue(identity, IssuerTestProbe.Matching(identity)).Sequence;

        Assert.Equal(IssueKind.Deduped, issuer.Issue(identity));

        issuer.ArmForce();

        Assert.Equal(
            IssueKind.Stale,
            issuer.TryIssue(identity, () => new ViewProbe(newer, IssuerTestProbe.NoReaders, default)).Kind);

        long secondSequence = issuer.TryIssue(identity, IssuerTestProbe.Matching(identity)).Sequence;

        Assert.Equal(firstSequence + 1, secondSequence);
    }

    [Fact]
    public void TryIssue_WhenDeduped_DoesNotRunTheProbe()
    {
        ViewRequestIssuer issuer = new();
        ViewIdentity identity = IdentityFor(EventLogId.Create());
        int probeRuns = 0;

        Assert.Equal(IssueKind.Issued, issuer.Issue(identity));

        IssueOutcome outcome = issuer.TryIssue(
            identity,
            () =>
            {
                probeRuns++;

                return new ViewProbe(identity, IssuerTestProbe.NoReaders, default);
            });

        Assert.Equal(IssueKind.Deduped, outcome.Kind);
        Assert.Equal(0, probeRuns);
    }

    [Fact]
    public void TryIssue_WhenDeduped_PreservesTheLastIssuedIdentity()
    {
        ViewRequestIssuer issuer = new();
        ViewIdentity identity = IdentityFor(EventLogId.Create());

        Assert.Equal(IssueKind.Issued, issuer.Issue(identity));
        Assert.Equal(IssueKind.Deduped, issuer.Issue(identity));
        Assert.Equal(IssueKind.Deduped, issuer.Issue(identity));
    }

    [Fact]
    public void TryIssue_WhenTheProbeThrows_PreservesArmedEntitlements()
    {
        ViewRequestIssuer issuer = new();
        ViewIdentity identity = IdentityFor(EventLogId.Create());

        Assert.Equal(IssueKind.Issued, issuer.Issue(identity));

        issuer.ArmRecovery();

        Assert.Throws<InvalidOperationException>(
            () => issuer.TryIssue(identity, () => throw new InvalidOperationException("reader capture failed")));

        IssueOutcome outcome = issuer.TryIssue(identity, IssuerTestProbe.Matching(identity));

        Assert.Equal(IssueKind.Issued, outcome.Kind);
        Assert.True(outcome.ClearFault);
    }

    [Fact]
    public void TryIssue_WhenTheProbeThrows_PreservesTheLastIssuedIdentity()
    {
        ViewRequestIssuer issuer = new();
        ViewIdentity issued = IdentityFor(EventLogId.Create());
        ViewIdentity attempted = IdentityFor(EventLogId.Create());

        Assert.Equal(IssueKind.Issued, issuer.Issue(issued));

        Assert.Throws<InvalidOperationException>(
            () => issuer.TryIssue(attempted, () => throw new InvalidOperationException("reader capture failed")));

        Assert.Equal(IssueKind.Deduped, issuer.Issue(issued));
    }

    [Fact]
    public void TryIssue_WhenTheProbedLiveIdentityMovedPast_IsStaleAndPreservesArmedEntitlements()
    {
        ViewRequestIssuer issuer = new();
        ViewIdentity identity = IdentityFor(EventLogId.Create());
        ViewIdentity newer = IdentityFor(EventLogId.Create());

        Assert.Equal(IssueKind.Issued, issuer.Issue(identity));

        issuer.ArmRecovery();

        Assert.Equal(
            IssueKind.Stale,
            issuer.TryIssue(identity, () => new ViewProbe(newer, IssuerTestProbe.NoReaders, default)).Kind);

        IssueOutcome outcome = issuer.TryIssue(identity, IssuerTestProbe.Matching(identity));

        Assert.Equal(IssueKind.Issued, outcome.Kind);
        Assert.True(outcome.ClearFault);
    }

    [Fact]
    public void TryIssue_WhenTheProbedLiveIdentityMovedPast_IsStaleAndPreservesTheLastIssuedIdentity()
    {
        ViewRequestIssuer issuer = new();
        ViewIdentity issued = IdentityFor(EventLogId.Create());
        ViewIdentity attempted = IdentityFor(EventLogId.Create());
        ViewIdentity newer = IdentityFor(EventLogId.Create());

        Assert.Equal(IssueKind.Issued, issuer.Issue(issued));

        Assert.Equal(
            IssueKind.Stale,
            issuer.TryIssue(attempted, () => new ViewProbe(newer, IssuerTestProbe.NoReaders, default)).Kind);

        Assert.Equal(IssueKind.Deduped, issuer.Issue(issued));
    }

    [Fact]
    public void ViewIdentity_OrderingInputSet_IsPinned_SoANewInputForcesSyncEffectCoverageReview()
    {
        EventLogId? activeLogId = null;
        ImmutableArray<EventLogId> scope = [];
        ColumnName? orderBy = null;
        bool isDescending = false;
        ColumnName? groupBy = null;
        bool isGroupDescending = false;
        bool timelineVisible = false;
        bool isMultiLogDisplay = false;
        Filter filter = new(null, []);

        _ = new ViewIdentity(
            activeLogId: activeLogId,
            scope: scope,
            orderBy: orderBy,
            isDescending: isDescending,
            groupBy: groupBy,
            isGroupDescending: isGroupDescending,
            timelineVisible: timelineVisible,
            isMultiLogDisplay: isMultiLogDisplay,
            filter: filter);

        ParameterInfo[] parameters = typeof(ViewIdentity)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single()
            .GetParameters();

        Assert.Equal(
            [
                nameof(activeLogId), nameof(scope), nameof(orderBy), nameof(isDescending), nameof(groupBy),
                nameof(isGroupDescending), nameof(timelineVisible), nameof(isMultiLogDisplay), nameof(filter)
            ],
            parameters.Select(parameter => parameter.Name));

        Assert.Equal(
            [
                typeof(EventLogId?), typeof(ImmutableArray<EventLogId>), typeof(ColumnName?), typeof(bool),
                typeof(ColumnName?), typeof(bool), typeof(bool), typeof(bool), typeof(Filter)
            ],
            parameters.Select(parameter => parameter.ParameterType));
    }

    private static LogTableState DefaultOrderedState(EventLogId activeId, int displayedLogCount)
    {
        List<LogView> tabs = [new LogView(activeId) { LogName = "Application" }];

        for (int index = 1; index < displayedLogCount; index++)
        {
            tabs.Add(new LogView(EventLogId.Create()) { LogName = "Other" });
        }

        return new LogTableState { ActiveEventLogId = activeId, EventTables = [.. tabs] };
    }

    private static ViewIdentity IdentityFor(EventLogId logId) =>
        new LogTableState
        {
            ActiveEventLogId = logId,
            EventTables = [new LogView(logId) { LogName = "Application" }]
        }.ViewIdentity;
}
