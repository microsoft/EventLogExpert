// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.FilterLenses;
using Fluxor;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.FilterLenses;

public sealed class FilterLensCommandsTests
{
    [Fact]
    public void ClearLenses_DispatchesClear()
    {
        var dispatcher = Substitute.For<IDispatcher>();

        new FilterLensCommands(dispatcher).ClearLenses();

        dispatcher.Received(1).Dispatch(Arg.Any<ClearFilterLensesAction>());
    }

    [Fact]
    public void RemoveLens_DispatchesRemove()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var lens = new FilterLens { Label = "x", Kind = LensKind.Property };

        new FilterLensCommands(dispatcher).RemoveLens(lens);

        dispatcher.Received(1).Dispatch(Arg.Is<RemoveFilterLensAction>(action => action.Lens == lens));
    }

    [Fact]
    public void ShowParentActivity_Guid_DispatchesActivityIdExcludeLensWithParentLabel()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var relatedActivityId = Guid.NewGuid();

        new FilterLensCommands(dispatcher).ShowParentActivity(relatedActivityId);

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action =>
            action.Lens.Label == $"Parent Activity = {relatedActivityId}" &&
            action.Lens.ExcludeFilters.Count == 1 &&
            action.Lens.ExcludeFilters[0].Compiled != null &&
            action.Lens.ExcludeFilters[0].ComparisonText!.Contains("ActivityId") &&
            !action.Lens.ExcludeFilters[0].ComparisonText!.Contains("RelatedActivityId")));
    }

    [Fact]
    public void ShowParentActivity_NullId_DoesNotDispatch()
    {
        var dispatcher = Substitute.For<IDispatcher>();

        new FilterLensCommands(dispatcher).ShowParentActivity(null);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<PushFilterLensAction>());
    }

    [Fact]
    public void ShowParentActivity_WithOriginLog_TagsLensWithThatLog()
    {
        var dispatcher = Substitute.For<IDispatcher>();

        new FilterLensCommands(dispatcher).ShowParentActivity(Guid.NewGuid(), "LogA");

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action => action.Lens.OriginLog == "LogA"));
    }

    [Fact]
    public void ShowRelatedByActivityId_Guid_DispatchesPushWithCompiledExcludeLens()
    {
        var dispatcher = Substitute.For<IDispatcher>();

        new FilterLensCommands(dispatcher).ShowRelatedByActivityId(Guid.NewGuid());

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action =>
            action.Lens.Kind == LensKind.Property &&
            action.Lens.ExcludeFilters.Count == 1 &&
            action.Lens.ExcludeFilters[0].IsExcluded &&
            action.Lens.ExcludeFilters[0].Compiled != null));
    }

    [Fact]
    public void ShowRelatedByActivityId_NullId_DoesNotDispatch()
    {
        var dispatcher = Substitute.For<IDispatcher>();

        new FilterLensCommands(dispatcher).ShowRelatedByActivityId(null);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<PushFilterLensAction>());
    }

    [Fact]
    public void ShowRelatedByActivityId_WithOriginLog_TagsLensWithThatLog()
    {
        var dispatcher = Substitute.For<IDispatcher>();

        new FilterLensCommands(dispatcher).ShowRelatedByActivityId(Guid.NewGuid(), "LogA");

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action => action.Lens.OriginLog == "LogA"));
    }

    [Fact]
    public void ShowRelatedByRelatedActivityId_Guid_DispatchesPushWithRelatedActivityIdExcludeLens()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var id = Guid.NewGuid();

        new FilterLensCommands(dispatcher).ShowRelatedByRelatedActivityId(id);

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action =>
            action.Lens.Label == $"Related Activity ID = {id}" &&
            action.Lens.ExcludeFilters.Count == 1 &&
            action.Lens.ExcludeFilters[0].IsExcluded &&
            action.Lens.ExcludeFilters[0].Compiled != null &&
            action.Lens.ExcludeFilters[0].ComparisonText!.Contains("RelatedActivityId")));
    }

    [Fact]
    public void ShowRelatedByRelatedActivityId_NullId_DoesNotDispatch()
    {
        var dispatcher = Substitute.For<IDispatcher>();

        new FilterLensCommands(dispatcher).ShowRelatedByRelatedActivityId(null);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<PushFilterLensAction>());
    }

    [Fact]
    public void ShowRelatedByRelatedActivityId_WithOriginLog_TagsLensWithThatLog()
    {
        var dispatcher = Substitute.For<IDispatcher>();

        new FilterLensCommands(dispatcher).ShowRelatedByRelatedActivityId(Guid.NewGuid(), "LogA");

        dispatcher.Received(1).Dispatch(Arg.Is<PushFilterLensAction>(action => action.Lens.OriginLog == "LogA"));
    }
}
