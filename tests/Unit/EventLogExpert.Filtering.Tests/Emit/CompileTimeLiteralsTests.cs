// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Emit;

namespace EventLogExpert.Filtering.Tests.Emit;

public sealed class CompileTimeLiteralsTests
{
    [Fact]
    public void CoerceToGuidArray_WhenValueIsNotAGuid_DropsIt()
    {
        var first = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var result = CompileTimeLiterals.CoerceToGuidArray([first.ToString(), "not-a-guid"]);

        Assert.Equal(new[] { first }, result);
    }

    [Fact]
    public void CoerceToGuidArray_WhenValuesParse_ReturnsAllInOrder()
    {
        var first = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var second = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var result = CompileTimeLiterals.CoerceToGuidArray([first.ToString(), second.ToString()]);

        Assert.Equal(new[] { first, second }, result);
    }

    [Fact]
    public void CoerceToIntArray_WhenAllValuesFailToParse_ReturnsEmptyArray()
    {
        var result = CompileTimeLiterals.CoerceToIntArray(["abc", "def"]);

        Assert.Empty(result);
    }

    [Fact]
    public void CoerceToIntArray_WhenAllValuesParse_ReturnsAllInOrder()
    {
        var result = CompileTimeLiterals.CoerceToIntArray(["1", "2", "3"]);

        Assert.Equal(new[] { 1, 2, 3 }, result);
    }

    [Fact]
    public void CoerceToIntArray_WhenInputIsEmpty_ReturnsEmptyArray()
    {
        var result = CompileTimeLiterals.CoerceToIntArray(Array.Empty<string>());

        Assert.Empty(result);
    }

    [Fact]
    public void CoerceToIntArray_WhenSomeValuesFailToParse_DropsOnlyTheFailures()
    {
        var result = CompileTimeLiterals.CoerceToIntArray(["1", "abc", "3", "4.5"]);

        Assert.Equal(new[] { 1, 3 }, result);
    }

    [Fact]
    public void CoerceToIntArray_WhenValuesHaveSurroundingWhitespace_TrimsBeforeParsing()
    {
        var result = CompileTimeLiterals.CoerceToIntArray(["  1  ", "\t2\t", " 3"]);

        Assert.Equal(new[] { 1, 2, 3 }, result);
    }

    [Fact]
    public void CoerceToLongArray_WhenValueExceedsIntRange_StillParses()
    {
        var result = CompileTimeLiterals.CoerceToLongArray(["1234567890123", "0"]);

        Assert.Equal(new[] { 1234567890123L, 0L }, result);
    }

    [Fact]
    public void CoerceToLongArray_WhenValueIsNotALong_DropsIt()
    {
        var result = CompileTimeLiterals.CoerceToLongArray(["100", "not-a-long", "200"]);

        Assert.Equal(new[] { 100L, 200L }, result);
    }

    [Fact]
    public void Snapshot_AlwaysReturnsANewArrayOfTheSameValues()
    {
        IReadOnlyList<string> source = ["a", "b", "c"];

        var first = CompileTimeLiterals.Snapshot(source);
        var second = CompileTimeLiterals.Snapshot(source);

        Assert.Equal(source, first);
        Assert.NotSame(first, second);
    }
}
