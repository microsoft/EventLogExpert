// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Structured;
using EventLogExpert.Eventing.TestUtils;
using EventLogExpert.Filtering.Parsing;

namespace EventLogExpert.Filtering.Tests.EventData;

/// <summary>
///     Behavioral coverage for EventData / UserData field-name wildcard globs. A glob (<c>EventData["*cert*"]</c> /
///     <c>UserData["*cert*"]</c>) is evaluated as if each field whose name/path matches the glob were its own OR'd filter
///     row, using the existing single-field per-value semantics. EventData is boolean (fully enumerated); UserData is the
///     tri-state, keep-visible on per-field truncation and on a capped (incomplete) field set.
/// </summary>
public sealed class FieldNameGlobTests
{
    [Fact]
    public void EventDataGlob_AnyOf_OrsAcrossMatches()
    {
        var predicate = CompileBool("(new[] {\"a\", \"b\"}).Contains(EventData[\"*cert*\"])");

        Assert.True(predicate(Event(("IssuerCert", "b"))));
        Assert.False(predicate(Event(("IssuerCert", "c"))));
    }

    [Fact]
    public void EventDataGlob_Contains_OrsAcrossMatches()
    {
        var predicate = CompileBool("EventData[\"*cert*\"].Contains(\"ab\", StringComparison.OrdinalIgnoreCase)");

        Assert.True(predicate(Event(("MyCert", "xabz"))));
        Assert.False(predicate(Event(("MyCert", "xyz"))));
        Assert.False(predicate(Event(("Other", "xabz")))); // matching value but non-matching name
    }
    // --- EventData glob (boolean, OR across matching fields). ---

    [Fact]
    public void EventDataGlob_Equal_OrsAcrossMatchingFields()
    {
        var predicate = CompileBool("EventData[\"*cert*\"] == \"x\"");

        Assert.True(predicate(Event(("IssuerCert", "x"), ("Subject", "y")))); // a cert-named field equals x
        Assert.True(predicate(Event(("Subject", "y"), ("RootCertName", "x")))); // a different cert field equals x
        Assert.False(predicate(Event(("IssuerCert", "y")))); // cert field present but wrong value
        Assert.False(predicate(Event(("Subject", "x")))); // no cert-named field
    }

    [Fact]
    public void EventDataGlob_NameMatch_IsCaseInsensitive()
    {
        var predicate = CompileBool("EventData[\"*CERT*\"] == \"x\"");

        Assert.True(predicate(Event(("issuercert", "x"))));
    }

    [Fact]
    public void EventDataGlob_NoEventData_NoMatch()
    {
        var predicate = CompileBool("EventData[\"*cert*\"] == \"x\"");

        Assert.Equal(EventDataKind.None, new ResolvedEvent("TestLog", LogPathType.Channel).EventData.Kind);
        Assert.False(predicate(new ResolvedEvent("TestLog", LogPathType.Channel)));
    }

    [Fact]
    public void EventDataGlob_NotEqual_IsPresentAndDiffersPerField()
    {
        var predicate = CompileBool("EventData[\"*cert*\"] != \"x\"");

        Assert.True(predicate(Event(("IssuerCert", "y")))); // present cert field, differs
        Assert.False(predicate(Event(("IssuerCert", "x")))); // present cert field, equal
        Assert.False(predicate(Event(("Subject", "y")))); // no cert field present -> no match
    }

    [Fact]
    public void EventDataGlob_NotEqual_MatchesWhenAnyMatchingFieldDiffers()
    {
        var predicate = CompileBool("EventData[\"*cert*\"] != \"x\"");

        // IssuerCert == x fails "!= x", but SubjectCert == y satisfies it; each match is its own OR'd row -> match.
        Assert.True(predicate(Event(("IssuerCert", "x"), ("SubjectCert", "y"))));
        // Every matching field equals x -> none satisfies "!= x" -> no match.
        Assert.False(predicate(Event(("IssuerCert", "x"), ("SubjectCert", "x"))));
    }

    [Fact]
    public void EventDataGlob_StarMatchesEveryField()
    {
        var predicate = CompileBool("EventData[\"*\"] == \"x\"");

        Assert.True(predicate(Event(("Anything", "x"))));
        Assert.False(predicate(Event(("Anything", "y"))));
    }

    [Fact]
    public void UserDataGlob_AnyOf_OrsAcrossMatchingPaths()
    {
        // Exercises the UserDataMultiEqualsNode routed through the glob matcher.
        var compiled = CompileEval("(new[] {\"a\", \"b\"}).Contains(UserData[\"*cert*\"])");

        Assert.Equal(FilterMatch.Match, compiled.Evaluate!(UserDataEvent(
            new UserDataField("IssuerCert", ["b"], IsTruncated: false))));
        Assert.Equal(FilterMatch.NoMatch, compiled.Evaluate!(UserDataEvent(
            new UserDataField("IssuerCert", ["c"], IsTruncated: false))));
    }

    [Fact]
    public void UserDataGlob_AttributePathGlob_Matches()
    {
        var compiled = CompileEval("UserData[\"*/@subjectName\"] == \"acme\"");

        Assert.Equal(FilterMatch.Match, compiled.Evaluate!(UserDataEvent(
            new UserDataField("X509Objects/Certificate/@subjectName", ["acme"], IsTruncated: false))));
    }

    [Fact]
    public void UserDataGlob_DefaultUserData_IsNoMatch_NoThrow() // B1
    {
        var compiled = CompileEval("UserData[\"*cert*\"] == \"x\"");

        Assert.Equal(FilterMatch.NoMatch, compiled.Evaluate!(new ResolvedEvent("TestLog", LogPathType.Channel)));
    }

    [Fact]
    public void UserDataGlob_DefaultUserDataButIncomplete_IsUnknown() // B1 + incomplete
    {
        var compiled = CompileEval("UserData[\"*cert*\"] == \"x\"");

        Assert.Equal(FilterMatch.Unknown, compiled.Evaluate!(
            new ResolvedEvent("TestLog", LogPathType.Channel) { UserDataIncomplete = true }));
    }

    // --- UserData glob (tri-state, OR across matching stored paths). ---

    [Fact]
    public void UserDataGlob_Equal_OrsAcrossMatchingPaths()
    {
        var compiled = CompileEval("UserData[\"*cert*\"] == \"x\"");

        Assert.Equal(FilterMatch.Match, compiled.Evaluate!(UserDataEvent(
            new UserDataField("IssuerCert", ["x"], IsTruncated: false),
            new UserDataField("Subject", ["y"], IsTruncated: false))));
        Assert.Equal(FilterMatch.NoMatch, compiled.Evaluate!(UserDataEvent(
            new UserDataField("IssuerCert", ["y"], IsTruncated: false))));
        Assert.Equal(FilterMatch.NoMatch, compiled.Evaluate!(UserDataEvent(
            new UserDataField("Subject", ["x"], IsTruncated: false)))); // no cert path
    }

    [Fact]
    public void UserDataGlob_MatchingTruncatedField_IsUnknown()
    {
        var compiled = CompileEval("UserData[\"*cert*\"] == \"x\"");

        // A matching path whose values were capped: a dropped value might equal x, so keep the row visible (Unknown).
        Assert.Equal(FilterMatch.Unknown, compiled.Evaluate!(UserDataEvent(
            new UserDataField("MyCert", ["y"], IsTruncated: true))));
    }

    [Fact]
    public void UserDataGlob_NoMatchingPathButIncomplete_IsUnknown()
    {
        var compiled = CompileEval("UserData[\"*cert*\"] == \"x\"");

        // A capped field set with no cert path present: a dropped cert path might have matched, so Unknown, not NoMatch.
        Assert.Equal(FilterMatch.Unknown, compiled.Evaluate!(new ResolvedEvent("TestLog", LogPathType.Channel)
        {
            UserData = [new UserDataField("Other", ["y"], IsTruncated: false)],
            UserDataIncomplete = true
        }));
    }

    [Fact]
    public void UserDataGlob_NotEqual_IsPresentAndDiffersPerPath()
    {
        var compiled = CompileEval("UserData[\"*cert*\"] != \"x\"");

        Assert.Equal(FilterMatch.Match, compiled.Evaluate!(UserDataEvent(
            new UserDataField("MyCert", ["y"], IsTruncated: false)))); // present cert path, differs
        Assert.Equal(FilterMatch.NoMatch, compiled.Evaluate!(UserDataEvent(
            new UserDataField("MyCert", ["x"], IsTruncated: false)))); // present cert path, equal
    }

    [Fact]
    public void UserDataGlob_NotEqual_MatchesWhenAnyMatchingPathDiffers()
    {
        var compiled = CompileEval("UserData[\"*cert*\"] != \"x\"");

        // One matching path equals x, another differs; the differing path satisfies "!= x" -> Match (OR-sugar).
        Assert.Equal(FilterMatch.Match, compiled.Evaluate!(UserDataEvent(
            new UserDataField("IssuerCert", ["x"], IsTruncated: false),
            new UserDataField("SubjectCert", ["y"], IsTruncated: false))));
        // Every matching path equals x -> no path satisfies "!= x" -> NoMatch.
        Assert.Equal(FilterMatch.NoMatch, compiled.Evaluate!(UserDataEvent(
            new UserDataField("IssuerCert", ["x"], IsTruncated: false),
            new UserDataField("SubjectCert", ["x"], IsTruncated: false))));
    }

    [Fact]
    public void UserDataGlob_NotEqual_PresentDifferingButIncomplete_IsUnknown()
    {
        // Regression pin (post-code review): on an incomplete (capped) event a present-differing matched field is Unknown
        // - a dropped value might equal x - not a decisive Match, exactly as the exact single-field path folds the
        // incomplete signal. This keeps an excluded row visible (fail-safe), and makes the glob the OR of exact rows.
        var compiled = CompileEval("UserData[\"*cert*\"] != \"x\"");

        var incomplete = new ResolvedEvent("TestLog", LogPathType.Channel)
        {
            UserData = [new UserDataField("MyCert", ["y"], IsTruncated: false)],
            UserDataIncomplete = true
        };

        Assert.Equal(FilterMatch.Unknown, compiled.Evaluate!(incomplete));

        // The glob's tri-state equals the exact single-field filter on that same path.
        var exact = CompileEval("UserData[\"MyCert\"] != \"x\"");
        Assert.Equal(exact.Evaluate!(incomplete), compiled.Evaluate!(incomplete));
    }

    [Fact]
    public void UserDataGlob_Predicate_HighlightsOnlyOnDecisiveMatch()
    {
        var compiled = CompileEval("UserData[\"*cert*\"] == \"x\"");

        Assert.True(compiled.Predicate(UserDataEvent(new UserDataField("MyCert", ["x"], IsTruncated: false))));
        Assert.False(compiled.Predicate(UserDataEvent(new UserDataField("MyCert", ["y"], IsTruncated: true)))); // Unknown
        Assert.False(compiled.Predicate(new ResolvedEvent("TestLog", LogPathType.Channel)));
    }

    [Fact]
    public void UserDataRepeatMarker_RoutesToExactMatcher_NotGlob() // N1
    {
        // "Root/Item[*]" carries the repeating-element marker, which ToStorageKey strips to "Root/Item"; it is an exact
        // lookup, NOT a name glob, so it matches only that stored path.
        var compiled = CompileEval("UserData[\"Root/Item[*]\"] == \"x\"");

        Assert.Equal(FilterMatch.Match, compiled.Evaluate!(UserDataEvent(
            new UserDataField("Root/Item", ["x"], IsTruncated: false))));
        Assert.Equal(FilterMatch.NoMatch, compiled.Evaluate!(UserDataEvent(
            new UserDataField("Root/Other", ["x"], IsTruncated: false))));
    }

    private static Func<ResolvedEvent, bool> CompileBool(string filter)
    {
        Assert.True(FilterParser.TryCompile(filter, out var compiled, out var error), error);

        return compiled.Predicate;
    }

    private static CompiledFilter CompileEval(string filter)
    {
        Assert.True(FilterParser.TryCompile(filter, out var compiled, out var error), error);

        return compiled;
    }

    private static ResolvedEvent Event(params (string Name, object? Value)[] fields) =>
        EventDataTestFactory.CreateEventWithData(fields);

    private static ResolvedEvent UserDataEvent(params UserDataField[] fields) =>
        new("TestLog", LogPathType.Channel) { UserData = [.. fields] };
}
