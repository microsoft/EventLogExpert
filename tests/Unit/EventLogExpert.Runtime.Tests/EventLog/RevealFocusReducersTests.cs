// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.EventLog;

namespace EventLogExpert.Runtime.Tests.EventLog;

public sealed class RevealFocusReducersTests
{
    private static readonly EventLogId s_logA = EventLogId.Create();
    private static readonly EventLogId s_logB = EventLogId.Create();

    [Fact]
    public void ReduceCloseAll_ClearsThePendingReveal()
    {
        var state = new EventLogState { PendingRevealFocus = new EventLocator(s_logA, 0, 7) };

        state = Reducers.ReduceCloseAll(state);

        Assert.Null(state.PendingRevealFocus);
    }

    [Fact]
    public void ReduceCloseLog_ClearsThePendingReveal_WhenItTargetsTheClosedLog()
    {
        var state = new EventLogState { PendingRevealFocus = new EventLocator(s_logA, 0, 7) };

        state = Reducers.ReduceCloseLog(state, new CloseLogAction(s_logA, "Application"));

        Assert.Null(state.PendingRevealFocus);
    }

    [Fact]
    public void ReduceCloseLog_KeepsThePendingReveal_WhenItTargetsADifferentLog()
    {
        var revealForA = new EventLocator(s_logA, 0, 7);
        var state = new EventLogState { PendingRevealFocus = revealForA };

        state = Reducers.ReduceCloseLog(state, new CloseLogAction(s_logB, "System"));

        Assert.Equal(revealForA, state.PendingRevealFocus);
    }

    [Fact]
    public void ReduceRequestRevealFocus_ANewerTarget_Supersedes()
    {
        var first = new EventLocator(s_logA, 0, 1);
        var second = new EventLocator(s_logA, 0, 2);
        var state = Reducers.ReduceRequestRevealFocus(new EventLogState(), new RequestRevealFocusAction(first));

        state = Reducers.ReduceRequestRevealFocus(state, new RequestRevealFocusAction(second));

        Assert.Equal(second, state.PendingRevealFocus);
    }

    [Fact]
    public void ReduceRequestRevealFocus_ReRequestingSameTarget_LeavesStateReferenceUnchanged()
    {
        var target = new EventLocator(s_logA, 0, 7);
        var state = Reducers.ReduceRequestRevealFocus(new EventLogState(), new RequestRevealFocusAction(target));

        var next = Reducers.ReduceRequestRevealFocus(state, new RequestRevealFocusAction(target));

        Assert.Same(state, next);
    }

    [Fact]
    public void ReduceRequestRevealFocus_SetsThePendingTarget()
    {
        var target = new EventLocator(s_logA, 0, 7);

        var state = Reducers.ReduceRequestRevealFocus(new EventLogState(), new RequestRevealFocusAction(target));

        Assert.Equal(target, state.PendingRevealFocus);
    }

    [Fact]
    public void ReduceRevealFocusConsumed_ClearsWhenTheTargetMatches()
    {
        var target = new EventLocator(s_logA, 0, 7);
        var state = Reducers.ReduceRequestRevealFocus(new EventLogState(), new RequestRevealFocusAction(target));

        state = Reducers.ReduceRevealFocusConsumed(state, new RevealFocusConsumedAction(target));

        Assert.Null(state.PendingRevealFocus);
    }

    [Fact]
    public void ReduceRevealFocusConsumed_DoesNotClearANewerTarget_WhenConsumingAnOlderOne()
    {
        var older = new EventLocator(s_logA, 0, 1);
        var newer = new EventLocator(s_logB, 0, 3);
        var state = new EventLogState { PendingRevealFocus = newer };

        state = Reducers.ReduceRevealFocusConsumed(state, new RevealFocusConsumedAction(older));

        Assert.Equal(newer, state.PendingRevealFocus);
    }
}
