// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Tests.TestUtils.Constants;
using System.Collections.Immutable;

namespace EventLogExpert.Filtering.Tests;

public sealed class BasicFilterDecomposerTests
{
    public static IEnumerable<object[]> ManyValueRoundTrips() =>
        // BasicFilterFormatter ignores Operator for Many (treats it as Equals-Any-Of). Restrict the
        // cross-product to (Equals, Many) — the only Many shape the formatter and decomposer agree on.
        from property in EachBasicFilterProperty()
        where property is not EventProperty.Description
            and not EventProperty.Xml
        let values = ManyValuesFor(property)
        where values is not null
        select new object[] { property, values };

    public static IEnumerable<object[]> SingleValueRoundTrips() =>
        from property in EachBasicFilterProperty()
        from op in EachSingleValueOperator()
        where SupportsSingleValueRoundTrip(property, op)
        let value = SingleValueFor(property, op)
        where value is not null
        select new object[] { property, op, value };

    [Fact]
    public void TryDecompose_WhenActivityIdNotContainsNestedInSubFilterChain_ReconstructsEquivalentBasicFilter()
    {
        // Pins NotContains-on-nullable-Guid reconstruction in a non-leading subfilter position.
        var original = new BasicFilter(
            Comparison(EventProperty.Id, ComparisonOperator.Equals, "100"),
            [
                new SubFilter(Comparison(EventProperty.ActivityId, ComparisonOperator.NotContains, "abc"), false),
                new SubFilter(Comparison(EventProperty.Source, ComparisonOperator.Equals, "TestSource"), false)
            ]);

        AssertCanonicalRoundTrip(original);
    }

    [Fact]
    public void TryDecompose_WhenAllUserIdOperatorVariantsInChain_ReconstructsEachUserIdSubFilter()
    {
        var original = new BasicFilter(
            Comparison(EventProperty.UserId, ComparisonOperator.Equals, "S-1-5-18"),
            [
                new SubFilter(Comparison(EventProperty.UserId, ComparisonOperator.NotEqual, "S-1-5-19"), false),
                new SubFilter(Comparison(EventProperty.UserId, ComparisonOperator.Contains, "5-18"), false),
                new SubFilter(Comparison(EventProperty.UserId, ComparisonOperator.NotContains, "5-99"), false)
            ]);

        AssertCanonicalRoundTrip(original);
    }

    [Fact]
    public void TryDecompose_WhenAndChainOfThree_ReconstructsConjunctionOfSubFilters()
    {
        var original = new BasicFilter(
            Comparison(EventProperty.Id, ComparisonOperator.Equals, "100"),
            [
                new SubFilter(Comparison(EventProperty.Source, ComparisonOperator.Equals, "TestSource"), false),
                new SubFilter(Comparison(EventProperty.Level, ComparisonOperator.Equals, "Error"), false)
            ]);

        AssertCanonicalRoundTrip(original);
    }

    [Fact]
    public void TryDecompose_WhenComparisonAgainstNullLiteral_RefusesUnencodableNull()
    {
        // BasicFilter has no encoding for null comparisons; reject any literal-null shape.
        var ok = BasicFilterDecomposer.TryDecompose("Source == null", out var decomposed);

        Assert.False(ok);
        Assert.Null(decomposed);
    }

    [Fact]
    public void TryDecompose_WhenContainsLacksOrdinalIgnoreCase_RefusesAsAdvancedOnly()
    {
        // Formatter always emits OIC; case-sensitive Contains is Advanced-only.
        var ok = BasicFilterDecomposer.TryDecompose("Source.Contains(\"Test\")", out var decomposed);

        Assert.False(ok);
        Assert.Null(decomposed);
    }

    [Fact]
    public void TryDecompose_WhenDoubleNegation_RefusesAsAdvancedOnly()
    {
        var ok = BasicFilterDecomposer.TryDecompose(
            "!!Source.Contains(\"X\", StringComparison.OrdinalIgnoreCase)",
            out var decomposed);

        Assert.False(ok);
        Assert.Null(decomposed);
    }

    [Fact]
    public void TryDecompose_WhenIdLiteralIsZeroPaddedString_NormalizesValueToCanonicalInt()
    {
        // Documents the Lowerer typed-coercion contract: "001" coerces to int 1, then re-stringifies as "1".
        // The decomposer therefore yields a BasicFilter whose canonical formatter output omits the padding.
        var ok = BasicFilterDecomposer.TryDecompose("Id == \"001\"", out var decomposed);

        Assert.True(ok);
        Assert.NotNull(decomposed);
        Assert.Equal(EventProperty.Id, decomposed.Comparison.Property);
        Assert.Equal(ComparisonOperator.Equals, decomposed.Comparison.Operator);
        Assert.Equal("1", decomposed.Comparison.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void TryDecompose_WhenInputEmptyOrWhitespace_RejectsBeforeTokenizing(string? filter)
    {
        var ok = BasicFilterDecomposer.TryDecompose(filter, out var decomposed);

        Assert.False(ok);
        Assert.Null(decomposed);
    }

    [Theory]
    [InlineData("Source == ")]
    [InlineData("Id ===  100")]
    [InlineData("Source == \"unterminated")]
    public void TryDecompose_WhenInputIsSyntacticallyInvalid_PropagatesParseFailureAsRefusal(string filter)
    {
        var ok = BasicFilterDecomposer.TryDecompose(filter, out var decomposed);

        Assert.False(ok);
        Assert.Null(decomposed);
    }

    [Fact]
    public void TryDecompose_WhenKeywordsAppearsInGenericComparisonShape_RefusesAsAdvancedOnly()
    {
        // The formatter never emits `Keywords == "X"` directly — it always wraps Keywords in `Keywords.Any(...)`.
        // The decomposer must refuse so an Advanced filter author's `Keywords == "X"` is not silently re-encoded
        // into `Keywords.Any(string.Equals(...))` with different semantics.
        var ok = BasicFilterDecomposer.TryDecompose("Keywords == \"Audit\"", out var decomposed);

        Assert.False(ok);
        Assert.Null(decomposed);
    }

    [Fact]
    public void TryDecompose_WhenKeywordsAppearsInGenericContainsShape_RefusesAsAdvancedOnly()
    {
        // The formatter emits Keywords Contains as `Keywords.Any(e => e.Contains(...))`, never as
        // `Keywords.ToString().Contains(...)`. Refuse so the vocabulary stays closed.
        var ok = BasicFilterDecomposer.TryDecompose(
            "Keywords.ToString().Contains(\"Audit\", StringComparison.OrdinalIgnoreCase)",
            out var decomposed);

        Assert.False(ok);
        Assert.Null(decomposed);
    }

    [Fact]
    public void TryDecompose_WhenKeywordsAppearsInGenericMultiEqualsShape_RefusesAsAdvancedOnly()
    {
        // The formatter emits Keywords Many as `Keywords.Any(e => (new[]{...}).Contains(e))`, never as
        // `(new[]{...}).Contains(Keywords)`. Refuse the latter so the round-trip vocabulary stays closed.
        var ok = BasicFilterDecomposer.TryDecompose(
            "(new[] {\"Audit\", \"System\"}).Contains(Keywords)",
            out var decomposed);

        Assert.False(ok);
        Assert.Null(decomposed);
    }

    [Fact]
    public void TryDecompose_WhenKeywordsNotContainsAppearsAfterOrJoin_ReconstructsMixedJoinSequence()
    {
        // Pins JoinWithAny=true reconstruction for the negated-Keywords subfilter shape after an OR boundary.
        var original = new BasicFilter(
            Comparison(EventProperty.Id, ComparisonOperator.Equals, "100"),
            [
                new SubFilter(Comparison(EventProperty.Keywords, ComparisonOperator.NotContains, "Audit"), true),
                new SubFilter(Comparison(EventProperty.Source, ComparisonOperator.Equals, "TestSource"), false)
            ]);

        AssertCanonicalRoundTrip(original);
    }

    [Theory]
    [MemberData(nameof(ManyValueRoundTrips))]
    public void TryDecompose_WhenManyValueFormatterShape_ReconstructsEquivalentBasicFilter(
        EventProperty property,
        ImmutableList<string> values)
    {
        var original = new BasicFilter(
            new FilterComparison
            {
                Property = property,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Many,
                Values = values
            },
            []);

        AssertCanonicalRoundTrip(original);
    }

    [Fact]
    public void TryDecompose_WhenMixedAndOrChain_ReconstructsMixedJoinSequence()
    {
        // Layout: A AND B OR C AND D — i.e., (A && B) || (C && D).
        var original = new BasicFilter(
            Comparison(EventProperty.Id, ComparisonOperator.Equals, "100"),
            [
                new SubFilter(Comparison(EventProperty.Source, ComparisonOperator.Equals, "TestSource"), false),
                new SubFilter(Comparison(EventProperty.Id, ComparisonOperator.Equals, "200"), true),
                new SubFilter(Comparison(EventProperty.Source, ComparisonOperator.Equals, "OtherSource"), false)
            ]);

        AssertCanonicalRoundTrip(original);
    }

    [Fact]
    public void TryDecompose_WhenNotWrapsBareComparison_RefusesAsAdvancedOnly()
    {
        // Formatter emits NotEqual directly as `!=`, not `!(... == ...)`. Reject the negated shape.
        var ok = BasicFilterDecomposer.TryDecompose(Constants.FilterNot, out var decomposed);

        Assert.False(ok);
        Assert.Null(decomposed);
    }

    [Fact]
    public void TryDecompose_WhenNotWrapsMultiEquals_RefusesAsAdvancedOnly()
    {
        var ok = BasicFilterDecomposer.TryDecompose(
            "!(new[] {\"100\", \"200\"}).Contains(Id.ToString())",
            out var decomposed);

        Assert.False(ok);
        Assert.Null(decomposed);
    }

    [Fact]
    public void TryDecompose_WhenOrChainOfThree_ReconstructsDisjunctionOfSubFilters()
    {
        var original = new BasicFilter(
            Comparison(EventProperty.Id, ComparisonOperator.Equals, "100"),
            [
                new SubFilter(Comparison(EventProperty.Id, ComparisonOperator.Equals, "200"), true),
                new SubFilter(Comparison(EventProperty.Id, ComparisonOperator.Equals, "300"), true)
            ]);

        AssertCanonicalRoundTrip(original);
    }

    [Fact]
    public void TryDecompose_WhenParenthesizedOrGroupAtBack_RefusesAsAdvancedOnly()
    {
        var ok = BasicFilterDecomposer.TryDecompose("Id == 100 && (Id == 200 || Id == 300)", out var decomposed);

        Assert.False(ok);
        Assert.Null(decomposed);
    }

    [Fact]
    public void TryDecompose_WhenParenthesizedOrGroupAtFront_RefusesAsAdvancedOnly()
    {
        // (A || B) && C parses as And(Or(A,B), C) — Or appears as an And-leaf. Decomposer rejects.
        var ok = BasicFilterDecomposer.TryDecompose(Constants.FilterParenthesizedMix, out var decomposed);

        Assert.False(ok);
        Assert.Null(decomposed);
    }

    [Theory]
    [InlineData("LogName == \"Application\"")]
    [InlineData("ComputerName == \"SERVER01\"")]
    [InlineData("RecordId == 1234567890123")]
    public void TryDecompose_WhenPropertyOutsideBasicFilterVocabulary_RefusesAsAdvancedOnly(string filter)
    {
        var ok = BasicFilterDecomposer.TryDecompose(filter, out var decomposed);

        Assert.False(ok);
        Assert.Null(decomposed);
    }

    [Theory]
    [InlineData(Constants.FilterIdGreaterThan100)]
    [InlineData(Constants.FilterIdLessThan100)]
    [InlineData(Constants.FilterIdGreaterThanOrEqual100)]
    [InlineData(Constants.FilterIdLessThanOrEqual100)]
    public void TryDecompose_WhenRelationalOperator_RefusesAsAdvancedOnly(string filter)
    {
        var ok = BasicFilterDecomposer.TryDecompose(filter, out var decomposed);

        Assert.False(ok);
        Assert.Null(decomposed);
    }

    [Theory]
    [MemberData(nameof(SingleValueRoundTrips))]
    public void TryDecompose_WhenSingleValueFormatterShape_ReconstructsEquivalentBasicFilter(
        EventProperty property,
        ComparisonOperator op,
        string value)
    {
        var original = new BasicFilter(
            new FilterComparison
            {
                Property = property,
                Operator = op,
                MatchMode = MatchMode.Single,
                Value = value
            },
            []);

        AssertCanonicalRoundTrip(original);
    }

    [Fact]
    public void TryDecompose_WhenSingleValueIsEmptyString_RefusesEmptyValue()
    {
        var ok = BasicFilterDecomposer.TryDecompose("Source == \"\"", out var decomposed);

        Assert.False(ok);
        Assert.Null(decomposed);
    }

    [Theory]
    [InlineData("Source.StartsWith(\"X\")")]
    [InlineData("Source.EndsWith(\"X\")")]
    public void TryDecompose_WhenStringMethodOutsideClosedVocabulary_RefusesAsAdvancedOnly(string filter)
    {
        var ok = BasicFilterDecomposer.TryDecompose(filter, out var decomposed);

        Assert.False(ok);
        Assert.Null(decomposed);
    }

    [Fact]
    public void TryDecompose_WhenUserIdGuardAppearsAfterOtherCondition_ReconstructsTrailingUserIdSubFilter()
    {
        // The formatter emits UserId as a 2-AND template; this used to fail to lower when the UserId guard
        // appeared in any position other than the top of an AndNode. The L2 lowerer extension fixes that.
        var original = new BasicFilter(
            Comparison(EventProperty.Id, ComparisonOperator.Equals, "100"),
            [new SubFilter(Comparison(EventProperty.UserId, ComparisonOperator.Equals, "S-1-5-18"), false)]);

        AssertCanonicalRoundTrip(original);
    }

    [Fact]
    public void TryDecompose_WhenUserIdGuardSurroundedByOtherConditions_ReconstructsCenterUserIdSubFilter()
    {
        var original = new BasicFilter(
            Comparison(EventProperty.Id, ComparisonOperator.Equals, "100"),
            [
                new SubFilter(Comparison(EventProperty.UserId, ComparisonOperator.Equals, "S-1-5-18"), false),
                new SubFilter(Comparison(EventProperty.Source, ComparisonOperator.Equals, "TestSource"), false)
            ]);

        AssertCanonicalRoundTrip(original);
    }

    [Theory]
    [InlineData("a\\b")]
    [InlineData("q\"q")]
    [InlineData("a\rb")]
    [InlineData("a\nb")]
    [InlineData("a\tb")]
    [InlineData("\\\"\r\n\t")]
    public void TryDecompose_WhenValueContainsEscapeSequences_RecoversOriginalEscapedString(string value)
    {
        var original = new BasicFilter(
            Comparison(EventProperty.Description, ComparisonOperator.Equals, value),
            []);

        Assert.True(BasicFilterFormatter.TryFormat(original, out var formatted));
        Assert.True(BasicFilterDecomposer.TryDecompose(formatted, out var decomposed));
        Assert.Equal(value, decomposed.Comparison.Value);
    }

    private static void AssertCanonicalRoundTrip(BasicFilter original)
    {
        Assert.True(
            BasicFilterFormatter.TryFormat(original, out var formattedOnce),
            "Formatter should accept the test fixture.");
        Assert.True(
            BasicFilterDecomposer.TryDecompose(formattedOnce, out var decomposed),
            $"Decomposer should accept formatter output: '{formattedOnce}'");
        Assert.True(
            BasicFilterFormatter.TryFormat(decomposed, out var formattedAgain),
            "Formatter should accept the decomposed BasicFilter.");

        Assert.Equal(formattedOnce, formattedAgain);
        AssertStructuralEquivalence(original, decomposed);
    }

    private static void AssertComparisonStructurallyEqual(FilterComparison expected, FilterComparison actual)
    {
        Assert.Equal(expected.Property, actual.Property);
        Assert.Equal(expected.Operator, actual.Operator);
        Assert.Equal(expected.MatchMode, actual.MatchMode);

        // Single mode normalizes typed-field strings (e.g., "001" -> "1"); accept either canonical form.
        Assert.Equal(expected.Value, actual.Value);

        // ImmutableList<T> uses reference equality in record equality; assert sequence equality directly.
        Assert.Equal<IEnumerable<string>>(expected.Values, actual.Values);
    }

    private static void AssertStructuralEquivalence(BasicFilter expected, BasicFilter actual)
    {
        AssertComparisonStructurallyEqual(expected.Comparison, actual.Comparison);
        Assert.Equal(expected.SubFilters.Count, actual.SubFilters.Count);

        for (var i = 0; i < expected.SubFilters.Count; i++)
        {
            Assert.Equal(expected.SubFilters[i].JoinWithAny, actual.SubFilters[i].JoinWithAny);
            AssertComparisonStructurallyEqual(expected.SubFilters[i].Comparison, actual.SubFilters[i].Comparison);
        }
    }

    private static FilterComparison Comparison(EventProperty property, ComparisonOperator op, string value) =>
        new()
        {
            Property = property,
            Operator = op,
            MatchMode = MatchMode.Single,
            Value = value
        };

    private static IEnumerable<EventProperty> EachBasicFilterProperty() =>
        Enum.GetValues<EventProperty>();

    private static IEnumerable<ComparisonOperator> EachSingleValueOperator() =>
        Enum.GetValues<ComparisonOperator>();

    private static ImmutableList<string>? ManyValuesFor(EventProperty property) =>
        property switch
        {
            EventProperty.Id => ["100", "200"],
            EventProperty.ActivityId =>
            [
                "00000000-0000-0000-0000-000000000001",
                "00000000-0000-0000-0000-000000000002"
            ],
            EventProperty.Level => ["Error", "Warning"],
            EventProperty.Keywords => ["Audit", "System"],
            EventProperty.Source => ["TestSource", "OtherSource"],
            EventProperty.TaskCategory => ["System", "Security"],
            EventProperty.ProcessId => ["4", "8"],
            EventProperty.ThreadId => ["8", "16"],
            EventProperty.UserId => ["S-1-5-18", "S-1-5-19"],
            _ => null
        };

    private static string? SingleValueFor(EventProperty property, ComparisonOperator op) =>
        property switch
        {
            EventProperty.Id => "100",
            EventProperty.ActivityId when op is ComparisonOperator.Equals or ComparisonOperator.NotEqual =>
                "00000000-0000-0000-0000-000000000001",
            EventProperty.ActivityId => "abc",
            EventProperty.Level => "Error",
            EventProperty.Keywords => "Audit",
            EventProperty.Source => "TestSource",
            EventProperty.TaskCategory => "System",
            EventProperty.ProcessId => "4",
            EventProperty.ThreadId => "8",
            EventProperty.UserId => "S-1-5-18",
            EventProperty.Description => "An error occurred",
            EventProperty.Xml => "<x/>",
            _ => null
        };

    private static bool SupportsSingleValueRoundTrip(EventProperty property, ComparisonOperator op)
    {
        // Pre-existing formatter <-> lowerer asymmetry: the formatter emits `ProcessId.Contains(...)` for
        // ProcessId/ThreadId Contains/NotContains (no `.ToString()`), but the lowerer's Contains-on-string branch
        // only matches when the property resolves to a string. End-to-end this combination has never been
        // representable; the decomposer correctly rejects it. Out of scope for L2 to widen the lowerer.
        if (op is ComparisonOperator.Contains or ComparisonOperator.NotContains
            && property is EventProperty.ProcessId or EventProperty.ThreadId)
        {
            return false;
        }

        return true;
    }
}
