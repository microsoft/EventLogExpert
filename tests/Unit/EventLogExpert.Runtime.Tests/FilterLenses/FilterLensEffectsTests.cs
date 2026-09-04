// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.FilterLibrary;
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

        dispatcher.Received(1).Dispatch(Arg.Is<RemoveLensesForLogAction>(action =>
            action != null && action.LogName == "LogA"));
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
    public async Task HandlePromoteAll_CommitsEveryPromotableLens_WithoutAnnouncing()
    {
        var keep = FilterLensFactory.ForActivityId(Guid.NewGuid())!;
        var time = FilterLensFactory.ForTimeWindow(DateTime.UtcNow, TimeSpan.FromMinutes(5), TimeZoneInfo.Utc);
        var (effects, dispatcher, announcer) =
            CreateEffectsWithAnnouncer(new FilterLensState { Lenses = [keep, time] }, new FilterPaneState());

        await effects.HandlePromoteAll(dispatcher);

        // One commit per lens; the breadcrumb announces once at the UI layer, so the effect itself stays silent.
        dispatcher.Received(1).Dispatch(Arg.Is<CommitPromotedLensAction>(action => action != null && action.Id == keep.Id));
        dispatcher.Received(1).Dispatch(Arg.Is<CommitPromotedLensAction>(action => action != null && action.Id == time.Id));
        announcer.DidNotReceive().AnnounceLensKept(Arg.Any<FilterLensLabel>());
    }

    [Fact]
    public async Task HandlePromoteAll_CommitsExcludeCriteria_NotPositiveIncludes()
    {
        // A keep lens must promote via its AND-narrowing exclude-of-complement (which intersects with sibling lenses),
        // not its positive PromoteForm include (which would OR into a union across multiple promoted lenses).
        var keep = FilterLensFactory.ForActivityId(Guid.NewGuid())!;
        var (effects, dispatcher) = CreateEffects(new FilterLensState { Lenses = [keep] }, new FilterPaneState());

        await effects.HandlePromoteAll(dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<CommitPromotedLensAction>(action =>
            action != null && action.Id == keep.Id &&
            action.Filters.Count == keep.ExcludeFilters.Count && action.Filters.All(filter => filter.IsExcluded)));
    }

    [Fact]
    public async Task HandlePromoteAll_SkipsNonPromotableLens()
    {
        var real = FilterLensFactory.ForActivityId(Guid.NewGuid())!;
        var degenerate = new FilterLens
        {
            Label = new FilterLensLabel.PropertyComparison(EventProperty.Source, IsEqual: true, "empty"),
            Kind = LensKind.Property,
            ExcludeFilters = []
        };
        var (effects, dispatcher) =
            CreateEffects(new FilterLensState { Lenses = [real, degenerate] }, new FilterPaneState());

        await effects.HandlePromoteAll(dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<CommitPromotedLensAction>(action => action != null && action.Id == real.Id));
        dispatcher.DidNotReceive().Dispatch(Arg.Is<CommitPromotedLensAction>(action => action != null && action.Id == degenerate.Id));
    }

    [Fact]
    public async Task HandlePromote_AbsentLens_DoesNotAnnounceOrCommit()
    {
        var lens = FilterLensFactory.ForActivityId(Guid.NewGuid())!;
        var (effects, dispatcher, announcer) =
            CreateEffectsWithAnnouncer(new FilterLensState { Lenses = [lens] }, new FilterPaneState());

        await effects.HandlePromote(new PromoteFilterLensAction(FilterLensId.Create()), dispatcher);

        announcer.DidNotReceive().AnnounceLensKept(Arg.Any<FilterLensLabel>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<CommitPromotedLensAction>());
    }

    [Fact]
    public async Task HandlePromote_DegenerateLens_DoesNotAnnounceOrCommit()
    {
        var lens = new FilterLens
        {
            Label = new FilterLensLabel.PropertyComparison(EventProperty.Source, IsEqual: true, "empty"),
            Kind = LensKind.Property,
            ExcludeFilters = []
        };
        var (effects, dispatcher, announcer) =
            CreateEffectsWithAnnouncer(new FilterLensState { Lenses = [lens] }, new FilterPaneState());

        await effects.HandlePromote(new PromoteFilterLensAction(lens.Id), dispatcher);

        announcer.DidNotReceive().AnnounceLensKept(Arg.Any<FilterLensLabel>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<CommitPromotedLensAction>());
    }

    [Fact]
    public async Task HandlePromote_HideLens_DispatchesExcludeFallback()
    {
        var lens = FilterLensFactory.ForExcludedValue(EventProperty.Source, "Contoso")!;
        var (effects, dispatcher, announcer) =
            CreateEffectsWithAnnouncer(new FilterLensState { Lenses = [lens] }, new FilterPaneState());

        await effects.HandlePromote(new PromoteFilterLensAction(lens.Id), dispatcher);

        announcer.Received(1).AnnounceLensKept(Arg.Any<FilterLensLabel>());

        // A hide lens has no positive promote form, so it falls back to its natural exclude.
        dispatcher.Received(1).Dispatch(Arg.Is<CommitPromotedLensAction>(action =>
            action != null && action.Id == lens.Id && action.Filters.Count == 1 &&
            action.Filters[0].IsExcluded && action.Window == null));
    }

    [Fact]
    public async Task HandlePromote_KeepOnlyLens_AnnouncesAndDispatchesPositiveInclude()
    {
        var lens = FilterLensFactory.ForActivityId(Guid.NewGuid())!;
        var (effects, dispatcher, announcer) =
            CreateEffectsWithAnnouncer(new FilterLensState { Lenses = [lens] }, new FilterPaneState());

        await effects.HandlePromote(new PromoteFilterLensAction(lens.Id), dispatcher);

        announcer.Received(1).AnnounceLensKept(lens.Label);

        // A keep-only lens promotes as a single POSITIVE INCLUDE (== value), not the transient exclude-of-complement.
        dispatcher.Received(1).Dispatch(Arg.Is<CommitPromotedLensAction>(action =>
            action != null && action.Id == lens.Id && action.Filters.Count == 1 &&
            !action.Filters[0].IsExcluded && action.Filters[0].Compiled != null && action.Window == null));
    }

    [Fact]
    public async Task HandlePromote_TimeWindowLens_DispatchesCommitWithWindow()
    {
        var lens = FilterLensFactory.ForTimeWindow(DateTime.UtcNow, TimeSpan.FromMinutes(5), TimeZoneInfo.Utc);
        var (effects, dispatcher, announcer) =
            CreateEffectsWithAnnouncer(new FilterLensState { Lenses = [lens] }, new FilterPaneState());

        await effects.HandlePromote(new PromoteFilterLensAction(lens.Id), dispatcher);

        announcer.Received(1).AnnounceLensKept(Arg.Any<FilterLensLabel>());
        dispatcher.Received(1).Dispatch(Arg.Is<CommitPromotedLensAction>(action =>
            action != null && action.Id == lens.Id && action.Filters.IsEmpty &&
            action.Window != null && action.Window.IsEnabled));
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
            action != null &&
            action.Filter.Filters.Count == 2 &&
            action.Filter.Filters.Count(filter => filter.IsExcluded) == 1 &&
            action.Filter.Filters.Count(filter => !filter.IsExcluded) == 1));

        // No round-trip: no FilterPane-mutating action fires, and the base pane state is unchanged.
        dispatcher.DidNotReceive().Dispatch(Arg.Any<AddFilterAction>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<SetFilterAction>());
        Assert.Single(paneState.Filters);
        Assert.False(paneState.Filters[0].IsExcluded);
    }

    [Fact]
    public async Task HandleSaveLensesAsGroup_BlankName_DoesNothing()
    {
        var keep = FilterLensFactory.ForActivityId(Guid.NewGuid())!;
        var (effects, dispatcher) = CreateEffects(new FilterLensState { Lenses = [keep] }, new FilterPaneState());

        await effects.HandleSaveLensesAsGroup(new SaveLensesAsGroupAction("   "), dispatcher);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<SaveFilterSetAction>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<RemoveFilterLensAction>());
    }

    [Fact]
    public async Task HandleSaveLensesAsGroup_MixedStack_SavesOnlyValueLensAndLeavesAllLensesActive()
    {
        var keep = FilterLensFactory.ForActivityId(Guid.NewGuid())!;
        var time = FilterLensFactory.ForTimeWindow(DateTime.UtcNow, TimeSpan.FromMinutes(5), TimeZoneInfo.Utc);
        var (effects, dispatcher) =
            CreateEffects(new FilterLensState { Lenses = [keep, time] }, new FilterPaneState());

        await effects.HandleSaveLensesAsGroup(new SaveLensesAsGroupAction("My Group"), dispatcher);

        // Only the value lens contributes to the saved set (the time-window lens carries no SavedFilters). All lenses
        // are left active so the user keeps the current view.
        dispatcher.Received(1).Dispatch(Arg.Is<SaveFilterSetAction>(action => action != null && action.Filters.Count == 1));
        dispatcher.DidNotReceive().Dispatch(Arg.Any<RemoveFilterLensAction>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<ClearFilterLensesAction>());
    }

    [Fact]
    public async Task HandleSaveLensesAsGroup_OnlyTimeWindowLenses_DoesNothing()
    {
        var time = FilterLensFactory.ForTimeWindow(DateTime.UtcNow, TimeSpan.FromMinutes(5), TimeZoneInfo.Utc);
        var (effects, dispatcher) = CreateEffects(new FilterLensState { Lenses = [time] }, new FilterPaneState());

        await effects.HandleSaveLensesAsGroup(new SaveLensesAsGroupAction("My Group"), dispatcher);

        // A time-window lens carries no SavedFilters, so there is nothing to persist and it stays active.
        dispatcher.DidNotReceive().Dispatch(Arg.Any<SaveFilterSetAction>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<RemoveFilterLensAction>());
    }

    [Fact]
    public async Task HandleSaveLensesAsGroup_ValueLenses_SavesIntersectionAndLeavesLensesActive()
    {
        var keep = FilterLensFactory.ForActivityId(Guid.NewGuid())!;
        var hide = FilterLensFactory.ForExcludedValue(EventProperty.Source, "Contoso")!;
        var (effects, dispatcher) =
            CreateEffects(new FilterLensState { Lenses = [keep, hide] }, new FilterPaneState());

        await effects.HandleSaveLensesAsGroup(new SaveLensesAsGroupAction("My Group"), dispatcher);

        // Both lenses contribute their AND-narrowing EXCLUDE criteria - NOT positive includes, which would OR into a
        // union broader than the intersected lens stack the user sees.
        dispatcher.Received(1).Dispatch(Arg.Is<SaveFilterSetAction>(action =>
            action != null && action.Name == "My Group" &&
            action.Filters.Count == 2 && action.Filters.All(filter => filter.IsExcluded)));

        // The lenses stay active after saving (matching the other save-to-filter-set flows); nothing is cleared.
        dispatcher.DidNotReceive().Dispatch(Arg.Any<RemoveFilterLensAction>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<ClearFilterLensesAction>());
    }

    private static SavedFilter Compile(string text) =>
        SavedFilter.TryCreate(text, isEnabled: true)
        ?? throw new InvalidOperationException($"test filter failed to compile: {text}");

    private static (LensEffects Effects, IDispatcher Dispatcher) CreateEffects(
        FilterLensState lensState,
        FilterPaneState paneState)
    {
        var (effects, dispatcher, _) = CreateEffectsWithAnnouncer(lensState, paneState);

        return (effects, dispatcher);
    }

    private static (LensEffects Effects, IDispatcher Dispatcher, IAnnouncementService Announcer) CreateEffectsWithAnnouncer(
        FilterLensState lensState,
        FilterPaneState paneState)
    {
        var lens = Substitute.For<IState<FilterLensState>>();
        lens.Value.Returns(lensState);

        var pane = Substitute.For<IState<FilterPaneState>>();
        pane.Value.Returns(paneState);

        var announcer = Substitute.For<IAnnouncementService>();

        return (new LensEffects(lens, pane, announcer), Substitute.For<IDispatcher>(), announcer);
    }
}
