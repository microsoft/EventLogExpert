// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.TestUtils.Constants;
using System.Collections.Immutable;

namespace EventLogExpert.Filtering.Tests.Basic;

public sealed class BasicFilterDecomposerTests
{
    public static IEnumerable<object[]> ManyOperatorRoundTrips() =>
        from property in new[]
        {
            EventProperty.Source, EventProperty.Level, EventProperty.LogName,
            EventProperty.TaskCategory, EventProperty.UserId, EventProperty.Opcode
        }
        from op in new[] { ComparisonOperator.Contains, ComparisonOperator.NotEqual, ComparisonOperator.NotContains }
        select new object[] { property, op };

    public static IEnumerable<object[]> ManyValueRoundTrips() =>
        from property in EachBasicFilterProperty()
        where property is not EventProperty.Description
            and not EventProperty.Xml
        let values = ManyValuesFor(property)
        where values is not null
        select new object[] { property, values };

    public static IEnumerable<object[]> SingleValueRoundTrips() =>
        from property in EachBasicFilterProperty()
        from op in EachSingleValueOperator()
        let value = SingleValueFor(property, op)
        where value is not null
        select new object[] { property, op, value };

    [Fact]
    public void TryDecompose_EventDataContainsAny_RoundTripsCanonically()
    {
        var original = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.EventData,
                EventDataFieldName = "NewProcessName",
                Operator = ComparisonOperator.Contains,
                MatchMode = MatchMode.Many,
                Values = ImmutableList.Create("mshta.exe", "wscript.exe", "cscript.exe")
            },
            []);

        AssertCanonicalRoundTrip(original);
    }

    [Theory]
    [MemberData(nameof(ManyOperatorRoundTrips))]
    public void TryDecompose_ManyWithOperator_RoundTripsCanonically(EventProperty property, ComparisonOperator op)
    {
        var values = ManyValuesFor(property) ?? ImmutableList.Create("alpha", "beta");

        var original = new BasicFilter(
            new FilterComparison
            {
                Property = property,
                Operator = op,
                MatchMode = MatchMode.Many,
                Values = values
            },
            []);

        AssertCanonicalRoundTrip(original);
    }

    [Fact]
    public void TryDecompose_UserDataContainsAny_RoundTripsCanonically()
    {
        var original = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.UserData,
                UserDataFieldName = "Foo/Bar",
                Operator = ComparisonOperator.Contains,
                MatchMode = MatchMode.Many,
                Values = ImmutableList.Create("alpha", "beta")
            },
            []);

        AssertCanonicalRoundTrip(original);
    }

    [Fact]
    public void TryDecompose_WhenActivityIdNotContainsNestedInSubFilterChain_ReconstructsEquivalentBasicFilter()
    {
        var original = new BasicFilter(
            Comparison(EventProperty.Id, ComparisonOperator.Equals, "100"),
            [
                new FilterPredicate(Comparison(EventProperty.ActivityId, ComparisonOperator.NotContains, "abc"), false),
                new FilterPredicate(Comparison(EventProperty.Source, ComparisonOperator.Equals, "TestSource"), false)
            ]);

        AssertCanonicalRoundTrip(original);
    }

    [Fact]
    public void TryDecompose_WhenAllUserIdOperatorVariantsInChain_ReconstructsEachUserIdSubFilter()
    {
        var original = new BasicFilter(
            Comparison(EventProperty.UserId, ComparisonOperator.Equals, "S-1-5-18"),
            [
                new FilterPredicate(Comparison(EventProperty.UserId, ComparisonOperator.NotEqual, "S-1-5-19"), false),
                new FilterPredicate(Comparison(EventProperty.UserId, ComparisonOperator.Contains, "5-18"), false),
                new FilterPredicate(Comparison(EventProperty.UserId, ComparisonOperator.NotContains, "5-99"), false)
            ]);

        AssertCanonicalRoundTrip(original);
    }

    [Fact]
    public void TryDecompose_WhenAndChainOfThree_ReconstructsConjunctionOfSubFilters()
    {
        var original = new BasicFilter(
            Comparison(EventProperty.Id, ComparisonOperator.Equals, "100"),
            [
                new FilterPredicate(Comparison(EventProperty.Source, ComparisonOperator.Equals, "TestSource"), false),
                new FilterPredicate(Comparison(EventProperty.Level, ComparisonOperator.Equals, "Error"), false)
            ]);

        AssertCanonicalRoundTrip(original);
    }

    [Fact]
    public void TryDecompose_WhenComparisonAgainstNullLiteral_RefusesUnencodableNull()
    {
        var ok = BasicFilterDecomposer.TryDecompose("Source == null", out var decomposed);

        Assert.False(ok);
        Assert.Null(decomposed);
    }

    [Fact]
    public void TryDecompose_WhenContainsLacksOrdinalIgnoreCase_RefusesAsAdvancedOnly()
    {
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
        // Formatter wraps Keywords in `Keywords.Any(...)`; refusing the generic shape prevents silent
        // re-encoding into different semantics on round-trip.
        var ok = BasicFilterDecomposer.TryDecompose("Keywords == \"Audit\"", out var decomposed);

        Assert.False(ok);
        Assert.Null(decomposed);
    }

    [Fact]
    public void TryDecompose_WhenKeywordsAppearsInGenericContainsShape_RefusesAsAdvancedOnly()
    {
        var ok = BasicFilterDecomposer.TryDecompose(
            "Keywords.ToString().Contains(\"Audit\", StringComparison.OrdinalIgnoreCase)",
            out var decomposed);

        Assert.False(ok);
        Assert.Null(decomposed);
    }

    [Fact]
    public void TryDecompose_WhenKeywordsAppearsInGenericMultiEqualsShape_RefusesAsAdvancedOnly()
    {
        var ok = BasicFilterDecomposer.TryDecompose(
            "(new[] {\"Audit\", \"System\"}).Contains(Keywords)",
            out var decomposed);

        Assert.False(ok);
        Assert.Null(decomposed);
    }

    [Fact]
    public void TryDecompose_WhenKeywordsNotContainsAppearsAfterOrJoin_ReconstructsMixedJoinSequence()
    {
        var original = new BasicFilter(
            Comparison(EventProperty.Id, ComparisonOperator.Equals, "100"),
            [
                new FilterPredicate(Comparison(EventProperty.Keywords, ComparisonOperator.NotContains, "Audit"), true),
                new FilterPredicate(Comparison(EventProperty.Source, ComparisonOperator.Equals, "TestSource"), false)
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
        var original = new BasicFilter(
            Comparison(EventProperty.Id, ComparisonOperator.Equals, "100"),
            [
                new FilterPredicate(Comparison(EventProperty.Source, ComparisonOperator.Equals, "TestSource"), false),
                new FilterPredicate(Comparison(EventProperty.Id, ComparisonOperator.Equals, "200"), true),
                new FilterPredicate(Comparison(EventProperty.Source, ComparisonOperator.Equals, "OtherSource"), false)
            ]);

        AssertCanonicalRoundTrip(original);
    }

    [Fact]
    public void TryDecompose_WhenNotWrapsBareComparison_RefusesAsAdvancedOnly()
    {
        var ok = BasicFilterDecomposer.TryDecompose(FilterTestConstants.FilterNot, out var decomposed);

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
                new FilterPredicate(Comparison(EventProperty.Id, ComparisonOperator.Equals, "200"), true),
                new FilterPredicate(Comparison(EventProperty.Id, ComparisonOperator.Equals, "300"), true)
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
        // Parser yields And(Or(A,B), C) — the leading Or-leaf is outside the BasicFilter vocabulary.
        var ok = BasicFilterDecomposer.TryDecompose(FilterTestConstants.FilterParenthesizedMix, out var decomposed);

        Assert.False(ok);
        Assert.Null(decomposed);
    }

    [Theory]
    [InlineData("ComputerName == \"SERVER01\"")]
    [InlineData("RecordId == 1234567890123")]
    public void TryDecompose_WhenPropertyOutsideBasicFilterVocabulary_RefusesAsAdvancedOnly(string filter)
    {
        var ok = BasicFilterDecomposer.TryDecompose(filter, out var decomposed);

        Assert.False(ok);
        Assert.Null(decomposed);
    }

    [Theory]
    [InlineData(FilterTestConstants.FilterIdGreaterThan100)]
    [InlineData(FilterTestConstants.FilterIdLessThan100)]
    [InlineData(FilterTestConstants.FilterIdGreaterThanOrEqual100)]
    [InlineData(FilterTestConstants.FilterIdLessThanOrEqual100)]
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
    public void TryDecompose_WhenUserIdGuardAppearsAfterOtherPredicate_ReconstructsTrailingUserIdSubFilter()
    {
        var original = new BasicFilter(
            Comparison(EventProperty.Id, ComparisonOperator.Equals, "100"),
            [new FilterPredicate(Comparison(EventProperty.UserId, ComparisonOperator.Equals, "S-1-5-18"), false)]);

        AssertCanonicalRoundTrip(original);
    }

    [Fact]
    public void TryDecompose_WhenUserIdGuardSurroundedByOtherPredicates_ReconstructsCenterUserIdSubFilter()
    {
        var original = new BasicFilter(
            Comparison(EventProperty.Id, ComparisonOperator.Equals, "100"),
            [
                new FilterPredicate(Comparison(EventProperty.UserId, ComparisonOperator.Equals, "S-1-5-18"), false),
                new FilterPredicate(Comparison(EventProperty.Source, ComparisonOperator.Equals, "TestSource"), false)
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

        // Typed fields canonicalize Single-mode values (e.g., "001" → "1").
        Assert.Equal(expected.Value, actual.Value);

        // ImmutableList<T> equality is reference-based on records; compare sequences directly.
        Assert.Equal<IEnumerable<string>>(expected.Values, actual.Values);

        // The dynamic field name (EventData) / storage-key path (UserData) must survive the round-trip; a
        // formatter/decomposer pair consistently using the wrong field would otherwise pass value/operator equality.
        Assert.Equal(expected.EventDataFieldName, actual.EventDataFieldName);
        Assert.Equal(expected.UserDataFieldName, actual.UserDataFieldName);
    }

    private static void AssertStructuralEquivalence(BasicFilter expected, BasicFilter actual)
    {
        AssertComparisonStructurallyEqual(expected.Comparison, actual.Comparison);
        Assert.Equal(expected.Predicates.Count, actual.Predicates.Count);

        for (var i = 0; i < expected.Predicates.Count; i++)
        {
            Assert.Equal(expected.Predicates[i].JoinWithAny, actual.Predicates[i].JoinWithAny);
            AssertComparisonStructurallyEqual(expected.Predicates[i].Comparison, actual.Predicates[i].Comparison);
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
            EventProperty.LogName => ["Application", "System"],
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
            EventProperty.LogName => "Application",
            _ => null
        };
}
