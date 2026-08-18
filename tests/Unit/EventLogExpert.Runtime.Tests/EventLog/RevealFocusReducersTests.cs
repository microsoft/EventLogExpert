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
        var state = new EventLogState { PendingRevealFocus = new RevealFocusRequest(new EventLocator(s_logA, 0, 7), true) };

        state = Reducers.ReduceCloseAll(state);

        Assert.Null(state.PendingRevealFocus);
    }

    [Fact]
    public void ReduceCloseLog_ClearsThePendingReveal_WhenItTargetsTheClosedLog()
    {
        var state = new EventLogState { PendingRevealFocus = new RevealFocusRequest(new EventLocator(s_logA, 0, 7), true) };

        state = Reducers.ReduceCloseLog(state, new CloseLogAction(s_logA, "Application"));

        Assert.Null(state.PendingRevealFocus);
    }

    [Fact]
    public void ReduceCloseLog_KeepsThePendingReveal_WhenItTargetsADifferentLog()
    {
        var revealForA = new RevealFocusRequest(new EventLocator(s_logA, 0, 7), true);
        var state = new EventLogState { PendingRevealFocus = revealForA };

        state = Reducers.ReduceCloseLog(state, new CloseLogAction(s_logB, "System"));

        Assert.Equal(revealForA, state.PendingRevealFocus);
    }

    [Fact]
    public void ReduceRequestRevealFocus_ADifferentWaitForView_Supersedes()
    {
        var target = new EventLocator(s_logA, 0, 7);
        var state = Reducers.ReduceRequestRevealFocus(new EventLogState(), new RequestRevealFocusAction(target, WaitForView: true));

        state = Reducers.ReduceRequestRevealFocus(state, new RequestRevealFocusAction(target, WaitForView: false));

        Assert.Equal(new RevealFocusRequest(target, false), state.PendingRevealFocus);
    }

    [Fact]
    public void ReduceRequestRevealFocus_ANewerTarget_Supersedes()
    {
        var first = new EventLocator(s_logA, 0, 1);
        var second = new EventLocator(s_logA, 0, 2);
        var state = Reducers.ReduceRequestRevealFocus(new EventLogState(), new RequestRevealFocusAction(first));

        state = Reducers.ReduceRequestRevealFocus(state, new RequestRevealFocusAction(second));

        Assert.Equal(new RevealFocusRequest(second, true), state.PendingRevealFocus);
    }

    [Fact]
    public void ReduceRequestRevealFocus_CarriesTheWaitForViewFlag()
    {
        var target = new EventLocator(s_logA, 0, 7);

        var state = Reducers.ReduceRequestRevealFocus(new EventLogState(), new RequestRevealFocusAction(target, WaitForView: false));

        Assert.Equal(new RevealFocusRequest(target, false), state.PendingRevealFocus);
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

        Assert.Equal(new RevealFocusRequest(target, true), state.PendingRevealFocus);
    }

    [Fact]
    public void ReduceRevealFocusConsumed_ClearsWhenTheTargetMatches()
    {
        var target = new EventLocator(s_logA, 0, 7);
        var state = Reducers.ReduceRequestRevealFocus(new EventLogState(), new RequestRevealFocusAction(target));

        state = Reducers.ReduceRevealFocusConsumed(state, new RevealFocusConsumedAction(new RevealFocusRequest(target, true)));

        Assert.Null(state.PendingRevealFocus);
    }

    [Fact]
    public void ReduceRevealFocusConsumed_DoesNotClearANewerTarget_WhenConsumingAnOlderOne()
    {
        var older = new EventLocator(s_logA, 0, 1);
        var newerLocator = new EventLocator(s_logB, 0, 3);
        var newer = new RevealFocusRequest(newerLocator, true);
        var state = new EventLogState { PendingRevealFocus = newer };

        state = Reducers.ReduceRevealFocusConsumed(state, new RevealFocusConsumedAction(new RevealFocusRequest(older, true)));

        Assert.Equal(newer, state.PendingRevealFocus);
    }

    [Fact]
    public void ReduceRevealFocusConsumed_DoesNotClear_WhenOnlyWaitForViewDiffers()
    {
        var target = new EventLocator(s_logA, 0, 7);
        var state = Reducers.ReduceRequestRevealFocus(new EventLogState(), new RequestRevealFocusAction(target, WaitForView: true));

        // A stale one-shot consumer (WaitForView=false) must not clear the newer wait-for-view (reload) request.
        state = Reducers.ReduceRevealFocusConsumed(state, new RevealFocusConsumedAction(new RevealFocusRequest(target, false)));

        Assert.Equal(new RevealFocusRequest(target, true), state.PendingRevealFocus);
    }
}
