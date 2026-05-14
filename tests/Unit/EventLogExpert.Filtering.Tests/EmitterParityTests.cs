// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Tests.TestUtils;
using EventLogExpert.Filtering.Tests.TestUtils.Constants;
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
        from index in Enumerable.Range(0, EventUtils.All.Count)
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
        Constants.FilterUserIdNotContainsService,
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
