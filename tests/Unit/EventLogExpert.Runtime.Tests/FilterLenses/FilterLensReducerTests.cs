// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.FilterLenses;

namespace EventLogExpert.Runtime.Tests.FilterLenses;

public sealed class FilterLensReducerTests
{
    [Fact]
    public void Clear_AlreadyEmpty_ReturnsSameStateInstance()
    {
        var state = new FilterLensState();

        var result = Reducers.ReduceClear(state);

        Assert.Same(state, result);
    }

    [Fact]
    public void Clear_EmptiesStack()
    {
        var state = new FilterLensState { Lenses = [Lens("a"), Lens("b")] };

        var result = Reducers.ReduceClear(state);

        Assert.Empty(result.Lenses);
    }

    [Fact]
    public void Push_AddsLensToTopOfStack()
    {
        var a = Lens("a");
        var b = Lens("b");
        var state = new FilterLensState { Lenses = [a] };

        var result = Reducers.ReducePush(state, new PushFilterLensAction(b));

        Assert.Equal(2, result.Lenses.Count);
        Assert.Same(a, result.Lenses[0]);
        Assert.Same(b, result.Lenses[1]);
    }

    [Fact]
    public void Remove_DuplicateContentLenses_RemovesOnlyTargetedInstance()
    {
        var first = Lens("duplicate");
        var second = Lens("duplicate");
        Assert.NotEqual(first.Id, second.Id);
        var state = new FilterLensState { Lenses = [first, second] };

        var result = Reducers.ReduceRemove(state, new RemoveFilterLensAction(second));

        Assert.Single(result.Lenses);
        Assert.Same(first, result.Lenses[0]);
    }

    [Fact]
    public void Remove_RemovesSpecificLens()
    {
        var a = Lens("a");
        var b = Lens("b");
        var state = new FilterLensState { Lenses = [a, b] };

        var result = Reducers.ReduceRemove(state, new RemoveFilterLensAction(a));

        Assert.Single(result.Lenses);
        Assert.Same(b, result.Lenses[0]);
    }

    [Fact]
    public void Remove_UnknownLens_ReturnsSameStateInstance()
    {
        var state = new FilterLensState { Lenses = [Lens("a")] };

        var result = Reducers.ReduceRemove(state, new RemoveFilterLensAction(Lens("other")));

        Assert.Same(state, result);
    }

    [Fact]
    public void RemoveForLog_NoMatch_ReturnsSameStateInstance()
    {
        var state = new FilterLensState { Lenses = [LensFromLog("a", "LogA")] };

        var result = Reducers.ReduceRemoveForLog(state, new RemoveLensesForLogAction("LogB"));

        Assert.Same(state, result);
    }

    [Fact]
    public void RemoveForLog_RemovesOnlyLensesFromThatLog()
    {
        var fromA1 = LensFromLog("a1", "LogA");
        var fromB = LensFromLog("b", "LogB");
        var fromA2 = LensFromLog("a2", "LogA");
        var state = new FilterLensState { Lenses = [fromA1, fromB, fromA2] };

        var result = Reducers.ReduceRemoveForLog(state, new RemoveLensesForLogAction("LogA"));

        Assert.Single(result.Lenses);
        Assert.Same(fromB, result.Lenses[0]);
    }

    private static FilterLens Lens(string label) =>
        new() { Label = label, Kind = LensKind.Property, ExcludeFilters = [] };

    private static FilterLens LensFromLog(string label, string originLog) =>
        new() { Label = label, Kind = LensKind.Property, OriginLog = originLog };
}
