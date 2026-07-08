// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Lowering;
using EventLogExpert.Filtering.Parsing;

namespace EventLogExpert.Filtering.Tests.Lowering;

public sealed class LowererUserIdChainTests
{
    [Fact]
    public void TryLower_WhenStrayUserIdNullCheckLacksValueTemplate_StillLowersAsRawNotEqualNull()
    {
        // No peephole match, so `UserId != null` lowers as a regular comparison against the null literal.
        var lowered = LowerOrThrow("Id == 100 && UserId != null");

        var top = Assert.IsType<AndNode>(lowered);
        Assert.IsType<ComparisonNode>(top.Left);
        var userIdCmp = Assert.IsType<ComparisonNode>(top.Right);
        Assert.Equal(ResolvedEventField.UserId, userIdCmp.Field);
        Assert.Equal(FilterBinaryOperator.NotEqual, userIdCmp.Op);
        Assert.Equal(TypedLiteralKind.Null, userIdCmp.Literal.Kind);
    }

    [Fact]
    public void TryLower_WhenTwoUserIdGuardsInChain_CollapsesEachIndependently()
    {
        var lowered = LowerOrThrow(
            "UserId != null && UserId.Value == \"S-1-5-18\" && UserId != null && UserId.Value != \"S-1-5-19\"");

        var top = Assert.IsType<AndNode>(lowered);
        var firstUserId = Assert.IsType<ComparisonNode>(top.Left);
        Assert.Equal(ResolvedEventField.UserId, firstUserId.Field);
        Assert.Equal(FilterBinaryOperator.Equal, firstUserId.Op);
        Assert.Equal("S-1-5-18", firstUserId.Literal.StringValue);

        var secondUserId = Assert.IsType<ComparisonNode>(top.Right);
        Assert.Equal(ResolvedEventField.UserId, secondUserId.Field);
        Assert.Equal(FilterBinaryOperator.NotEqual, secondUserId.Op);
        Assert.Equal("S-1-5-19", secondUserId.Literal.StringValue);
    }

    [Fact]
    public void TryLower_WhenUserIdGuardFollowedByContains_CollapsesToContainsNode()
    {
        var lowered = LowerOrThrow(
            "Id == 100 && UserId != null && UserId.Value.Contains(\"5-18\", StringComparison.OrdinalIgnoreCase)");

        var top = Assert.IsType<AndNode>(lowered);
        Assert.IsType<ComparisonNode>(top.Left);
        var contains = Assert.IsType<ContainsNode>(top.Right);
        Assert.Equal(ResolvedEventField.UserId, contains.Field);
        Assert.Equal("5-18", contains.Needle);
        Assert.True(contains.IgnoreCase);
    }

    [Fact]
    public void TryLower_WhenUserIdGuardFollowedByNegatedContains_CollapsesToNotContainsNode()
    {
        var lowered = LowerOrThrow(
            "UserId != null && !UserId.Value.Contains(\"5-99\", StringComparison.OrdinalIgnoreCase) && Id == 100");

        var top = Assert.IsType<AndNode>(lowered);
        var notContains = Assert.IsType<NotNode>(top.Left);
        var contains = Assert.IsType<ContainsNode>(notContains.Operand);
        Assert.Equal(ResolvedEventField.UserId, contains.Field);
        Assert.Equal("5-99", contains.Needle);
    }

    [Fact]
    public void TryLower_WhenUserIdGuardFollowsOtherCondition_CollapsesToTwoNodeAnd()
    {
        var lowered = LowerOrThrow("Id == 100 && UserId != null && UserId.Value == \"S-1-5-18\"");

        var top = Assert.IsType<AndNode>(lowered);
        var idCmp = Assert.IsType<ComparisonNode>(top.Left);
        Assert.Equal(ResolvedEventField.Id, idCmp.Field);
        var userIdCmp = Assert.IsType<ComparisonNode>(top.Right);
        Assert.Equal(ResolvedEventField.UserId, userIdCmp.Field);
        Assert.Equal("S-1-5-18", userIdCmp.Literal.StringValue);
    }

    [Fact]
    public void TryLower_WhenUserIdGuardIsStandaloneAnd_CollapsesToSingleComparison()
    {
        var lowered = LowerOrThrow("UserId != null && UserId.Value == \"S-1-5-18\"");

        var cmp = Assert.IsType<ComparisonNode>(lowered);
        Assert.Equal(ResolvedEventField.UserId, cmp.Field);
        Assert.Equal(FilterBinaryOperator.Equal, cmp.Op);
        Assert.Equal(TypedLiteralKind.String, cmp.Literal.Kind);
        Assert.Equal("S-1-5-18", cmp.Literal.StringValue);
    }

    [Fact]
    public void TryLower_WhenUserIdGuardPrecedesOtherCondition_CollapsesToTwoNodeAnd()
    {
        var lowered = LowerOrThrow("UserId != null && UserId.Value == \"S-1-5-18\" && Id == 100");

        var top = Assert.IsType<AndNode>(lowered);
        var userIdCmp = Assert.IsType<ComparisonNode>(top.Left);
        Assert.Equal(ResolvedEventField.UserId, userIdCmp.Field);
        var idCmp = Assert.IsType<ComparisonNode>(top.Right);
        Assert.Equal(ResolvedEventField.Id, idCmp.Field);
    }

    [Fact]
    public void TryLower_WhenUserIdGuardSurroundedByOtherConditions_CollapsesAtCorrectPosition()
    {
        var lowered = LowerOrThrow(
            "Id == 100 && UserId != null && UserId.Value == \"S-1-5-18\" && Source == \"X\"");

        // Left-assoc rebuild: ((Id, UserId), Source).
        var top = Assert.IsType<AndNode>(lowered);
        var sourceCmp = Assert.IsType<ComparisonNode>(top.Right);
        Assert.Equal(ResolvedEventField.Source, sourceCmp.Field);

        var inner = Assert.IsType<AndNode>(top.Left);
        var idCmp = Assert.IsType<ComparisonNode>(inner.Left);
        Assert.Equal(ResolvedEventField.Id, idCmp.Field);
        var userIdCmp = Assert.IsType<ComparisonNode>(inner.Right);
        Assert.Equal(ResolvedEventField.UserId, userIdCmp.Field);
    }

    private static FilterNode LowerOrThrow(string filter)
    {
        Assert.True(Tokenizer.TryTokenize(filter, out var tokens, out var tokError), tokError);
        Assert.True(Parser.TryParse(tokens, out var syntax, out var parseError), parseError);
        Assert.True(Lowerer.TryLower(syntax!, out var filterNode, out var lowerError), lowerError);

        return filterNode!;
    }
}
