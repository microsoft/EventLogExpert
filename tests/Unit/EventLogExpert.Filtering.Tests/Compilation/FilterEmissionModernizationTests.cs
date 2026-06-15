// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Compilation;
using EventLogExpert.Filtering.TestUtils;

namespace EventLogExpert.Filtering.Tests.Compilation;

public sealed class FilterEmissionModernizationTests
{
    // The formatter emits the bare (no-ToString) form, and that emitted text compiles + matches end to end.
    [Theory]
    [InlineData(EventProperty.Id)]
    [InlineData(EventProperty.ProcessId)]
    [InlineData(EventProperty.ThreadId)]
    public void Format_ThenCompile_NumericContains_OmitsToStringAndMatches(EventProperty property)
    {
        var source = new BasicFilter(
            new FilterComparison
            {
                Property = property,
                Operator = ComparisonOperator.Contains,
                MatchMode = MatchMode.Single,
                Value = "23"
            },
            []);

        Assert.True(BasicFilterFormatter.TryFormat(source, out var text));
        Assert.DoesNotContain(".ToString()", text);

        var success = FilterCompiler.TryCompile(text, out var compiled, out var error);

        Assert.True(success, error);
        Assert.NotNull(compiled);
        Assert.True(compiled.Predicate(
            FilterEventBuilder.CreateTestEvent(1234, processId: 1234, threadId: 1234)));
    }

    [Fact]
    public void TryCompile_BareActivityIdContains_CompilesAndMatches()
    {
        var success = FilterCompiler.TryCompile(
            "ActivityId.Contains(\"abcdef\", StringComparison.OrdinalIgnoreCase)",
            out var compiled,
            out var error);

        Assert.True(success, error);
        Assert.NotNull(compiled);

        var matching = FilterEventBuilder.CreateTestEvent(activityId: new Guid("abcdef00-0000-0000-0000-000000000000"));

        var nonMatching = FilterEventBuilder.CreateTestEvent(activityId: Guid.Empty);

        Assert.True(compiled.Predicate(matching));
        Assert.False(compiled.Predicate(nonMatching));
    }

    // Bare numeric/Guid Contains (no .ToString()) compiles AND executes. ProcessId/ThreadId/RecordId Contains
    // threw at emit before the EmitContains rework, so a decompose-only round-trip would have passed falsely.
    [Theory]
    [InlineData("Id.Contains(\"23\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData("ProcessId.Contains(\"23\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData("ThreadId.Contains(\"23\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData("RecordId.Contains(\"23\", StringComparison.OrdinalIgnoreCase)")]
    public void TryCompile_BareNumericContains_CompilesAndMatches(string filter)
    {
        var success = FilterCompiler.TryCompile(filter, out var compiled, out var error);

        Assert.True(success, error);
        Assert.NotNull(compiled);

        var matching = FilterEventBuilder.CreateTestEvent(1234, processId: 1234, threadId: 1234, recordId: 1234);
        var nonMatching = FilterEventBuilder.CreateTestEvent(99, processId: 99, threadId: 99, recordId: 99);

        Assert.True(compiled.Predicate(matching));
        Assert.False(compiled.Predicate(nonMatching));
    }

    [Fact]
    public void TryCompile_BareNumericNotContains_CompilesAndInverts()
    {
        var success = FilterCompiler.TryCompile(
            "!ProcessId.Contains(\"99\", StringComparison.OrdinalIgnoreCase)",
            out var compiled,
            out var error);

        Assert.True(success, error);
        Assert.NotNull(compiled);

        Assert.True(compiled.Predicate(FilterEventBuilder.CreateTestEvent(processId: 1234)));
        Assert.False(compiled.Predicate(FilterEventBuilder.CreateTestEvent(processId: 999)));
    }

    [Fact]
    public void TryCompile_LegacyQuotedEquals_MatchesIdenticallyToTypedForm()
    {
        Assert.True(FilterCompiler.TryCompile("Id == \"100\"", out var legacy, out var legacyError), legacyError);
        Assert.True(FilterCompiler.TryCompile("Id == 100", out var modern, out var modernError), modernError);
        Assert.NotNull(legacy);
        Assert.NotNull(modern);

        var match = FilterEventBuilder.CreateTestEvent(100);
        var noMatch = FilterEventBuilder.CreateTestEvent(200);

        Assert.True(legacy.Predicate(match));
        Assert.True(modern.Predicate(match));
        Assert.False(legacy.Predicate(noMatch));
        Assert.False(modern.Predicate(noMatch));
    }

    // BACK-COMPAT: saved filters + the dedup cache persisted the legacy string-form ComparisonText. Loading
    // them re-parses that text, which must still compile and NEVER throw after the modernization.
    [Theory]
    [InlineData("Id == \"100\"")]
    [InlineData("Id != \"100\"")]
    [InlineData("Id.ToString().Contains(\"10\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData("ActivityId.ToString().Contains(\"00\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData("(new[] {\"100\", \"200\"}).Contains(Id.ToString())")]
    public void TryCompile_LegacyStoredForms_CompileWithoutThrowing(string legacyText)
    {
        var exception = Record.Exception(() =>
        {
            var success = FilterCompiler.TryCompile(legacyText, out var compiled, out var error);

            Assert.True(success, error);
            Assert.NotNull(compiled);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void TryCompile_LegacyToStringContains_MatchesIdenticallyToBareForm()
    {
        var legacyText = "Id.ToString().Contains(\"23\", StringComparison.OrdinalIgnoreCase)";

        Assert.True(FilterCompiler.TryCompile(legacyText, out var legacy, out var legacyError), legacyError);
        Assert.True(
            FilterCompiler.TryCompile("Id.Contains(\"23\", StringComparison.OrdinalIgnoreCase)", out var bare, out var bareError),
            bareError);
        Assert.NotNull(legacy);
        Assert.NotNull(bare);

        var match = FilterEventBuilder.CreateTestEvent(1234);
        var noMatch = FilterEventBuilder.CreateTestEvent(99);

        Assert.True(legacy.Predicate(match));
        Assert.True(bare.Predicate(match));
        Assert.False(legacy.Predicate(noMatch));
        Assert.False(bare.Predicate(noMatch));
    }

    [Fact]
    public void TryCompile_NumericContains_HandlesIntBoundaryValueViaSpanFormat()
    {
        var success = FilterCompiler.TryCompile(
            "Id.Contains(\"214748\", StringComparison.OrdinalIgnoreCase)",
            out var compiled,
            out var error);

        Assert.True(success, error);
        Assert.NotNull(compiled);

        Assert.True(compiled.Predicate(FilterEventBuilder.CreateTestEvent(int.MaxValue)));
        Assert.False(compiled.Predicate(FilterEventBuilder.CreateTestEvent()));
    }

    // TimeCreated has no EmitContains arm; BOTH the bare and the legacy .ToString() Lowerer Contains branches must
    // reject it cleanly at lower (TryCompile false) rather than letting it reach the emitter and throw.
    [Theory]
    [InlineData("TimeCreated.Contains(\"2024\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData("TimeCreated.ToString().Contains(\"2024\", StringComparison.OrdinalIgnoreCase)")]
    public void TryCompile_TimeCreatedContains_IsRejectedWithoutThrowing(string filter)
    {
        var exception = Record.Exception(() =>
        {
            var success = FilterCompiler.TryCompile(filter, out var compiled, out _);

            Assert.False(success);
            Assert.Null(compiled);
        });

        Assert.Null(exception);
    }
}
