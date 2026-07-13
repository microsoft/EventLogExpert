// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.FilterPane;
using Fluxor;
using NSubstitute;
using LensEffects = EventLogExpert.Runtime.FilterLenses.Effects;

namespace EventLogExpert.Runtime.Tests.FilterLenses;

public sealed class FilterLensEffectsTests
{
    [Fact]
    public async Task HandleCloseAllLogs_NoLenses_DoesNothing()
    {
        var (effects, dispatcher) = CreateEffects(new FilterLensState(), new FilterPaneState());

        await effects.HandleCloseAllLogs(dispatcher);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<ClearFilterLensesAction>());
    }

    [Fact]
    public async Task HandleCloseAllLogs_WithActiveLenses_DispatchesClear()
    {
        var lens = FilterLensFactory.ForActivityId(Guid.NewGuid())!;
        var (effects, dispatcher) = CreateEffects(new FilterLensState { Lenses = [lens] }, new FilterPaneState());

        await effects.HandleCloseAllLogs(dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Any<ClearFilterLensesAction>());
    }

    [Fact]
    public async Task HandleLogClosedByUser_LensFromThatLog_DispatchesRemoveForLog()
    {
        var lens = FilterLensFactory.ForActivityId(Guid.NewGuid(), "LogA")!;
        var (effects, dispatcher) = CreateEffects(new FilterLensState { Lenses = [lens] }, new FilterPaneState());

        await effects.HandleLogClosedByUser(new LogClosedByUserAction(EventLogId.Create(), "LogA"), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<RemoveLensesForLogAction>(action => action.LogName == "LogA"));
    }

    [Fact]
    public async Task HandleLogClosedByUser_NoLensFromThatLog_DoesNothing()
    {
        var lens = FilterLensFactory.ForActivityId(Guid.NewGuid(), "LogA")!;
        var (effects, dispatcher) = CreateEffects(new FilterLensState { Lenses = [lens] }, new FilterPaneState());

        await effects.HandleLogClosedByUser(new LogClosedByUserAction(EventLogId.Create(), "LogB"), dispatcher);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<RemoveLensesForLogAction>());
    }

    [Fact]
    public async Task HandlePush_ComposesLensOntoBase_DispatchesApplyFilter_WithoutTouchingBase()
    {
        // The base is a single include (Level == Error). Pushing an ActivityId lens must dispatch an effective filter
        // that is base-include + lens-exclude, and must NOT write anything back into the persistent FilterPaneState.
        var baseInclude = Compile("Level == \"Error\"");
        var paneState = new FilterPaneState { Filters = [baseInclude] };
        var lens = FilterLensFactory.ForActivityId(Guid.NewGuid())!;

        var (effects, dispatcher) = CreateEffects(new FilterLensState { Lenses = [lens] }, paneState);

        await effects.HandlePush(new PushFilterLensAction(lens), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<ApplyFilterAction>(action =>
            action.Filter.Filters.Count == 2 &&
            action.Filter.Filters.Count(filter => filter.IsExcluded) == 1 &&
            action.Filter.Filters.Count(filter => !filter.IsExcluded) == 1));

        // No round-trip: no FilterPane-mutating action fires, and the base pane state is unchanged.
        dispatcher.DidNotReceive().Dispatch(Arg.Any<AddFilterAction>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<SetFilterAction>());
        Assert.Single(paneState.Filters);
        Assert.False(paneState.Filters[0].IsExcluded);
    }

    private static SavedFilter Compile(string text) =>
        SavedFilter.TryCreate(text, isEnabled: true)
        ?? throw new InvalidOperationException($"test filter failed to compile: {text}");

    private static (LensEffects Effects, IDispatcher Dispatcher) CreateEffects(
        FilterLensState lensState,
        FilterPaneState paneState)
    {
        var lens = Substitute.For<IState<FilterLensState>>();
        lens.Value.Returns(lensState);

        var pane = Substitute.For<IState<FilterPaneState>>();
        pane.Value.Returns(paneState);

        return (new LensEffects(lens, pane), Substitute.For<IDispatcher>());
    }
}
