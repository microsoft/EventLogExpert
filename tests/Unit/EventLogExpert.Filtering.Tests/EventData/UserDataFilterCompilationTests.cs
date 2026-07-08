// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Structured;
using EventLogExpert.Filtering.Parsing;

namespace EventLogExpert.Filtering.Tests.EventData;

/// <summary>
///     Compile coverage for UserData structured-path filtering: canonical-path normalization to a storage key, the
///     presence-required grammar mirror of EventData, negation folding (no NotNode wraps a UserData term), and the
///     tri-state predicate over stored <see cref="ResolvedEvent.UserData" />. Events carry hand-built fields whose values
///     and truncation flag drive each to Match / NoMatch / Unknown.
/// </summary>
public sealed class UserDataFilterCompilationTests
{
    private const string NonMatchingValue = "\u0001__no_match__";

    [Theory]
    [InlineData("UserData[\"Foo\"].Contains(\"x\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData("!UserData[\"Foo\"].Contains(\"x\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData("(new[] {\"a\", \"b\"}).Contains(UserData[\"Foo\"])")]
    public void ContainsAndAnyOf_Compile(string filter)
    {
        var compiled = Compile(filter);

        Assert.NotNull(compiled.Evaluate);
    }

    [Fact]
    public void Equal_EvaluatesTriStateFromStoredValue()
    {
        var compiled = Compile("UserData[\"Foo\"] == \"x\"");

        Assert.NotNull(compiled.Evaluate);
        Assert.Equal(FilterMatch.Match, compiled.Evaluate!(EventWith(Value("Foo", FilterMatch.Match, "x"))));
        Assert.Equal(FilterMatch.NoMatch, compiled.Evaluate!(EventWith(Value("Foo", FilterMatch.NoMatch, "x"))));
        Assert.Equal(FilterMatch.Unknown, compiled.Evaluate!(EventWith(Value("Foo", FilterMatch.Unknown, "x"))));
    }

    [Fact]
    public void EventWithoutStoredUserData_ReadsAsNoMatch()
    {
        var compiled = Compile("UserData[\"Foo\"] == \"x\"");

        // An event with no stored UserData leaves (and not flagged incomplete) is decisively absent: NoMatch, never a throw.
        Assert.Equal(FilterMatch.NoMatch, compiled.Evaluate!(new ResolvedEvent("TestLog", LogPathType.Channel)));
    }

    [Fact]
    public void MixedUserDataAndScalar_LiftsScalarTermTrivially()
    {
        var compiled = Compile("UserData[\"Foo\"] == \"x\" && Source == \"Contoso\"");

        var matchingSource = new ResolvedEvent("TestLog", LogPathType.Channel)
        {
            Source = "Contoso",
            UserData = [Value("Foo", FilterMatch.Match, "x")]
        };

        var wrongSource = matchingSource with { Source = "Other" };

        Assert.Equal(FilterMatch.Match, compiled.Evaluate!(matchingSource));
        Assert.Equal(FilterMatch.NoMatch, compiled.Evaluate!(wrongSource)); // scalar F short-circuits the AND
    }

    [Fact]
    public void NotEqual_ViaNegation_FoldsWithoutRenegatingUnknown()
    {
        var direct = Compile("UserData[\"Foo\"] != \"x\"");
        var negated = Compile("!(UserData[\"Foo\"] == \"x\")");

        // A truncated non-matching field makes "!= x" ambiguous (a dropped value might equal x), so Unknown propagates
        // unchanged; negation folds into the NotEqual operator (no NotNode), so the negated form equals the direct "!= x".
        var unknownEvent = EventWith(Value("Foo", FilterMatch.Unknown, "x"));

        Assert.Equal(FilterMatch.Unknown, negated.Evaluate!(unknownEvent));
        Assert.Equal(direct.Evaluate!(unknownEvent), negated.Evaluate!(unknownEvent));
    }

    [Theory]
    [InlineData("UserData[\"Event/UserData/Foo/Bar\"] == \"x\"")]
    [InlineData("UserData[\"Foo/Bar\"] == \"x\"")]
    public void Path_SpellingsResolveToSameStoredValue(string filter)
    {
        var compiled = Compile(filter);

        // Both spellings canonicalize to Event/UserData/Foo/Bar then to the storage key Foo/Bar, so each reads the same
        // stored field. ("UserData/Foo/Bar" is NOT equivalent: only a full Event/UserData/ envelope is treated as rooted.)
        var matching = EventWith(new UserDataField("Foo/Bar", ["x"], IsTruncated: false));

        Assert.NotNull(compiled.Evaluate);
        Assert.Equal(FilterMatch.Match, compiled.Evaluate!(matching));
    }

    // The highlight surface is the bool Predicate (= Evaluate == Match): a UserData highlight colors a row only on a
    // decisive Match, never on Unknown or an absent field, so a truncated row stays visible but uncolored.
    [Fact]
    public void Predicate_HighlightsOnlyOnDecisiveMatch()
    {
        var compiled = Compile("UserData[\"Foo\"] == \"x\"");

        Assert.True(compiled.Predicate(EventWith(Value("Foo", FilterMatch.Match, "x"))));
        Assert.False(compiled.Predicate(EventWith(Value("Foo", FilterMatch.Unknown, "x"))));
        Assert.False(compiled.Predicate(EventWith(Value("Foo", FilterMatch.NoMatch, "x"))));
        Assert.False(compiled.Predicate(new ResolvedEvent("TestLog", LogPathType.Channel)));
    }

    [Theory]
    [InlineData(FilterMatch.Match, FilterMatch.Match, FilterMatch.Match)]
    [InlineData(FilterMatch.Match, FilterMatch.NoMatch, FilterMatch.NoMatch)]
    [InlineData(FilterMatch.Match, FilterMatch.Unknown, FilterMatch.Unknown)]
    [InlineData(FilterMatch.NoMatch, FilterMatch.Unknown, FilterMatch.NoMatch)] // U ∧ F = F
    [InlineData(FilterMatch.Unknown, FilterMatch.Unknown, FilterMatch.Unknown)]
    public void TriStateAnd(FilterMatch left, FilterMatch right, FilterMatch expected)
    {
        var compiled = Compile("UserData[\"A\"] == \"x\" && UserData[\"B\"] == \"y\"");

        var @event = EventWith(Value("A", left, "x"), Value("B", right, "y"));

        Assert.Equal(expected, compiled.Evaluate!(@event));
    }

    [Theory]
    [InlineData(FilterMatch.NoMatch, FilterMatch.NoMatch, FilterMatch.NoMatch)]
    [InlineData(FilterMatch.NoMatch, FilterMatch.Match, FilterMatch.Match)]
    [InlineData(FilterMatch.NoMatch, FilterMatch.Unknown, FilterMatch.Unknown)]
    [InlineData(FilterMatch.Match, FilterMatch.Unknown, FilterMatch.Match)] // U ∨ T = T
    [InlineData(FilterMatch.Unknown, FilterMatch.Unknown, FilterMatch.Unknown)]
    public void TriStateOr(FilterMatch left, FilterMatch right, FilterMatch expected)
    {
        var compiled = Compile("UserData[\"A\"] == \"x\" || UserData[\"B\"] == \"y\"");

        var @event = EventWith(Value("A", left, "x"), Value("B", right, "y"));

        Assert.Equal(expected, compiled.Evaluate!(@event));
    }

    [Theory]
    [InlineData("UserData[\"Foo\"] < \"5\"")] // relational not supported
    [InlineData("UserData[\"Foo\"] >= \"5\"")]
    [InlineData("UserData[\"\"] == \"x\"")] // empty path
    [InlineData("UserData[\"   \"] == \"x\"")] // whitespace path
    [InlineData("UserData[5] == \"x\"")] // non-string path
    [InlineData("UserData[\"Foo/@\"] == \"x\"")] // malformed canonical path
    [InlineData("!(new[] {\"a\", \"b\"}).Contains(UserData[\"Foo\"])")] // none-of
    [InlineData("!(UserData[\"Foo\"] == \"a\" || UserData[\"Foo\"] == \"b\")")] // grouped negation
    [InlineData("!(UserData[\"Foo\"] == \"a\" && Source == \"x\")")] // grouped negation, mixed
    public void UnsupportedShapes_FailToCompile(string filter)
    {
        Assert.False(FilterParser.TryCompile(filter, out var compiled, out var error));
        Assert.Null(compiled);
        Assert.False(string.IsNullOrEmpty(error));
    }

    private static CompiledFilter Compile(string filter)
    {
        Assert.True(FilterParser.TryCompile(filter, out var compiled, out var error), error);

        return compiled;
    }

    private static ResolvedEvent EventWith(params UserDataField[] fields) =>
        new("TestLog", LogPathType.Channel) { UserData = [.. fields] };

    // Builds a stored field whose "== literal" comparison yields the requested tri-state: Match (literal present),
    // NoMatch (a non-matching value), or Unknown (a non-matching value on a truncated field, so a match may be dropped).
    private static UserDataField Value(string storageKey, FilterMatch state, string literal) => state switch
    {
        FilterMatch.Match => new UserDataField(storageKey, [literal], IsTruncated: false),
        FilterMatch.NoMatch => new UserDataField(storageKey, [NonMatchingValue], IsTruncated: false),
        FilterMatch.Unknown => new UserDataField(storageKey, [NonMatchingValue], IsTruncated: true),
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };
}
