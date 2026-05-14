// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Tests.TestUtils;
using EventLogExpert.Filtering.Tests.TestUtils.Constants;

namespace EventLogExpert.Filtering.Tests;

/// <summary>
///     Golden parity suite: for every filter expression in the closed grammar, both the legacy Dynamic.Core compiler
///     (<see cref="FilterCompiler" />) and the hand-rolled emitter (<see cref="FilterParser.TryCompile" />) must produce
///     <c>bool</c>-equal predicates and identical <c>RequiresXml</c> flags against every event fixture. A divergence on
///     any (filter, event) pair is the regression we ship N2 to prevent — the existing UI and end-to-end suites are
///     written against Dynamic.Core's behavior, so anything the new emitter does differently is a behavior change.
/// </summary>
public sealed class EmitterParityTests
{
    public static IEnumerable<object[]> ParityCases() =>
        from filter in ParityFilters()
        from index in Enumerable.Range(0, EventUtils.All.Count)
        select new object[] { filter, index };

    [Theory]
    [MemberData(nameof(ParityCases))]
    public void TryCompile_WhenAppliedToFixture_MatchesDynamicCorePredicateAndRequiresXmlFlag(
        string filter,
        int eventIndex)
    {
        var dynamicOk = FilterCompiler.TryCompile(filter, out var dynamicCompiled, out var dynamicError);
        var emitterOk = FilterParser.TryCompile(filter, out var emitterCompiled, out var emitterError);

        Assert.True(dynamicOk, $"Dynamic.Core failed to compile '{filter}': {dynamicError}");
        Assert.True(emitterOk, $"Emitter failed to compile '{filter}': {emitterError}");

        var evt = EventUtils.All[eventIndex];
        var dynamicResult = dynamicCompiled!.Predicate(evt);
        var emitterResult = emitterCompiled!.Predicate(evt);

        Assert.Equal(dynamicResult, emitterResult);
        Assert.Equal(dynamicCompiled.RequiresXml, emitterCompiled.RequiresXml);
    }

    [Fact]
    public void TryCompile_WhenExpressionFailsLowering_PropagatesDiagnostic()
    {
        var ok = FilterParser.TryCompile("Source.StartsWith(\"X\")", out var compiled, out var error);

        Assert.False(ok);
        Assert.Null(compiled);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryCompile_WhenExpressionFailsToTokenize_PropagatesDiagnostic()
    {
        var ok = FilterParser.TryCompile("Source == \"unterminated", out var compiled, out var error);

        Assert.False(ok);
        Assert.Null(compiled);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryCompile_WhenExpressionIsEmpty_ReturnsFalseWithDiagnostic()
    {
        var ok = FilterParser.TryCompile(string.Empty, out var compiled, out var error);

        Assert.False(ok);
        Assert.Null(compiled);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryCompile_WhenExpressionIsNull_ReturnsFalseWithDiagnostic()
    {
        var ok = FilterParser.TryCompile(null, out var compiled, out var error);

        Assert.False(ok);
        Assert.Null(compiled);
        Assert.NotNull(error);
    }

    private static IEnumerable<string> ParityFilters() =>
    [
        Constants.FilterIdEquals100,
        Constants.FilterIdEquals100QuotedRhs,
        Constants.FilterIdEquals200,
        Constants.FilterIdGreaterThan100,
        Constants.FilterIdLessThan100,
        Constants.FilterIdGreaterThanOrEqual100,
        Constants.FilterIdLessThanOrEqual100,
        Constants.FilterIdNotEquals100,
        Constants.FilterIdEquals100AndLevelError,
        Constants.FilterIdEquals100Or200,
        Constants.FilterLevelEqualsError,
        Constants.FilterComputerNameEqualsServer01,
        Constants.FilterSourceEqualsTestSource,
        Constants.FilterSourceContainsTest,
        Constants.FilterSourceContainsTestOic,
        Constants.FilterDescriptionContainsErrorOccurred,
        Constants.FilterTaskCategoryContainsSecurity,
        Constants.FilterXmlContainsData,
        Constants.FilterIdMultiEquals,
        Constants.FilterLevelMultiEquals,
        Constants.FilterSourceMultiEquals,
        Constants.FilterKeywordsEqualsAudit,
        Constants.FilterKeywordsContainsAudit,
        Constants.FilterKeywordsAnyOfAuditOrSystem,
        Constants.FilterUserIdEqualsLocalSystem,
        Constants.FilterUserIdNotEqualsLocalSystem,
        Constants.FilterUserIdContainsService,
        Constants.FilterTwoConditionAnd,
        Constants.FilterThreeConditionAnd,
        Constants.FilterFourConditionAnd,
        Constants.FilterThreeConditionOr,
        Constants.FilterFourConditionOr,
        Constants.FilterParenthesizedMix,
        Constants.FilterNot,
        Constants.FilterActivityIdEqualsZero,
        Constants.FilterActivityIdContains,
        Constants.FilterRecordIdEquals,
        Constants.FilterProcessIdEquals,
        Constants.FilterThreadIdEquals,
        Constants.FilterPerfWerSystemErrorReporting,
        Constants.FilterPerfUser32,
        Constants.FilterPerfEventLog,
        Constants.FilterPerfApplicationError,
        Constants.FilterPerfKernelPower,
        Constants.FilterPerfResourceExhaustion,
        Constants.FilterPerfSystemStart,
        Constants.FilterDescriptionEqualsBackslash,
        Constants.FilterDescriptionEqualsQuote,
        Constants.FilterDescriptionEqualsTab,
        Constants.FilterDescriptionEqualsNewline,
        Constants.FilterDescriptionEqualsCarriageReturn
    ];
}
