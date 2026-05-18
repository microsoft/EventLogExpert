// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Parsing;
using EventLogExpert.Filtering.Runtime;
using EventLogExpert.Filtering.Tests.TestUtils;
using EventLogExpert.Filtering.Tests.TestUtils.Constants;
using EventLogExpert.Filtering.TestUtils.Constants;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Dynamic.Core;
using System.Linq.Expressions;

namespace EventLogExpert.Filtering.Tests;

public sealed class EmitterParityTests
{
    private static readonly ParsingConfig s_dynamicCoreParsingConfig =
        new() { AllowEqualsAndToStringMethodsOnObject = true };

    public static IEnumerable<object[]> ParityCases() =>
        from filter in ParityFilters()
        from index in Enumerable.Range(0, FilterTestFixtures.All.Count)
        select new object[] { filter, index };

    [Theory]
    [MemberData(nameof(ParityCases))]
    public void TryCompile_WhenAppliedToFixture_MatchesDynamicCorePredicateAndRequiresXmlFlag(
        string filter,
        int eventIndex)
    {
        var dynamicOk = TryCompileViaDynamicCore(filter, out var dynamicCompiled, out var dynamicError);
        var emitterOk = FilterParser.TryCompile(filter, out var emitterCompiled, out var emitterError);

        Assert.True(dynamicOk, $"Dynamic.Core failed to compile '{filter}': {dynamicError}");
        Assert.True(emitterOk, $"Emitter failed to compile '{filter}': {emitterError}");

        var evt = FilterTestFixtures.All[eventIndex];
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
        FilterTestConstants.FilterIdEquals100,
        FilterTestConstants.FilterIdEquals100QuotedRhs,
        FilterTestConstants.FilterIdEquals200,
        FilterTestConstants.FilterIdGreaterThan100,
        FilterTestConstants.FilterIdLessThan100,
        FilterTestConstants.FilterIdGreaterThanOrEqual100,
        FilterTestConstants.FilterIdLessThanOrEqual100,
        FilterTestConstants.FilterIdNotEquals100,
        FilterTestConstants.FilterIdEquals100AndLevelError,
        FilterTestConstants.FilterIdEquals100Or200,
        FilterTestConstants.FilterLevelEqualsError,
        FilterTestConstants.FilterComputerNameEqualsServer01,
        FilterTestConstants.FilterSourceEqualsTestSource,
        FilterTestConstants.FilterSourceContainsTest,
        FilterTestConstants.FilterSourceContainsTestOic,
        FilterTestConstants.FilterDescriptionContainsErrorOccurred,
        FilterTestConstants.FilterTaskCategoryContainsSecurity,
        FilterTestConstants.FilterXmlContainsData,
        FilterTestConstants.FilterIdMultiEquals,
        FilterTestConstants.FilterLevelMultiEquals,
        FilterTestConstants.FilterSourceMultiEquals,
        FilterTestConstants.FilterKeywordsEqualsAudit,
        FilterTestConstants.FilterKeywordsContainsAudit,
        FilterTestConstants.FilterKeywordsAnyOfAuditOrSystem,
        FilterTestConstants.FilterUserIdEqualsLocalSystem,
        FilterTestConstants.FilterUserIdNotEqualsLocalSystem,
        FilterTestConstants.FilterUserIdContainsService,
        FilterTestConstants.FilterUserIdNotContainsService,
        FilterTestConstants.FilterTwoConditionAnd,
        FilterTestConstants.FilterThreeConditionAnd,
        FilterTestConstants.FilterFourConditionAnd,
        FilterTestConstants.FilterThreeConditionOr,
        FilterTestConstants.FilterFourConditionOr,
        FilterTestConstants.FilterParenthesizedMix,
        FilterTestConstants.FilterNot,
        FilterTestConstants.FilterActivityIdEqualsZero,
        FilterTestConstants.FilterActivityIdContains,
        FilterTestConstants.FilterRecordIdEquals,
        FilterTestConstants.FilterProcessIdEquals,
        FilterTestConstants.FilterThreadIdEquals,
        FilterTestConstants.FilterPerfWerSystemErrorReporting,
        FilterTestConstants.FilterPerfUser32,
        FilterTestConstants.FilterPerfEventLog,
        FilterTestConstants.FilterPerfApplicationError,
        FilterTestConstants.FilterPerfKernelPower,
        FilterTestConstants.FilterPerfResourceExhaustion,
        FilterTestConstants.FilterPerfSystemStart,
        FilterTestConstants.FilterDescriptionEqualsBackslash,
        FilterTestConstants.FilterDescriptionEqualsQuote,
        FilterTestConstants.FilterDescriptionEqualsTab,
        FilterTestConstants.FilterDescriptionEqualsNewline,
        FilterTestConstants.FilterDescriptionEqualsCarriageReturn
    ];

    /// <summary>
    ///     Compiles <paramref name="filter" /> via Dynamic.Core with the same parsing config the production
    ///     <c>FilterCompiler</c> used before N3. Lifted into the test (rather than calling <c>FilterCompiler</c>) so this
    ///     parity suite continues to perform a genuine cross-implementation comparison after N3 swaps <c>FilterCompiler</c> to
    ///     delegate to <c>FilterParser</c>. N4 deletes this helper alongside the package reference and replaces the comparison
    ///     with golden-output assertions.
    /// </summary>
    private static bool TryCompileViaDynamicCore(
        string filter,
        [NotNullWhen(true)] out CompiledFilter? compiled,
        [NotNullWhen(false)] out string? error)
    {
        compiled = null;
        error = null;

        try
        {
            var lambda = DynamicExpressionParser
                .ParseLambda<ResolvedEvent, bool>(s_dynamicCoreParsingConfig, false, filter);

            var visitor = new XmlMemberAccessVisitor();
            visitor.Visit(lambda);

            compiled = new CompiledFilter(lambda.Compile(), visitor.Found);

            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;

            return false;
        }
    }

    private sealed class XmlMemberAccessVisitor : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (!Found &&
                node.Member.Name == nameof(ResolvedEvent.Xml) &&
                node.Member.DeclaringType == typeof(ResolvedEvent))
            {
                Found = true;
            }

            return base.VisitMember(node);
        }
    }
}
