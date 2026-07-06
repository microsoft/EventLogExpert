// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Structured;
using EventLogExpert.Filtering.Persistence;

namespace EventLogExpert.Filtering.Tests.EventData;

/// <summary>
///     The aggregate side of UserData filtering: how <c>MatchesFilters</c> reads each filter's stored-field tri-state
///     and collapses it under include / exclude polarity (a truncated Unknown never silently hides a row), and how several
///     UserData filters over the same event each evaluate their own stored field independently.
/// </summary>
public sealed class UserDataFilterAggregateTests
{
    private const string NonMatchingValue = "\u0001__no_match__";

    [Fact]
    public void MatchesFilters_AbsentPathOnCompleteEvent_HidesUnderInclude()
    {
        var filters = new[] { UserDataFilter("UserData[\"Missing\"] == \"x\"", isExcluded: false) };

        var complete = new ResolvedEvent("TestLog", LogPathType.Channel)
        {
            UserData = [new UserDataField("Other", ["v"], IsTruncated: false)]
        };

        Assert.False(complete.MatchesFilters(filters));
    }

    // Distinct-path-cap fail-safe end to end: on an incomplete event an unstored path keeps the row visible under both
    // polarities (the accessor returns a truncated result the emitter reads as Unknown); on a complete event the same
    // absent path is decisive, so an include filter hides it.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MatchesFilters_AbsentPathOnIncompleteEvent_KeepsRowVisible(bool isExcluded)
    {
        var filters = new[] { UserDataFilter("UserData[\"Missing\"] == \"x\"", isExcluded) };

        var incomplete = new ResolvedEvent("TestLog", LogPathType.Channel)
        {
            UserData = [new UserDataField("Other", ["v"], IsTruncated: false)],
            UserDataIncomplete = true
        };

        Assert.True(incomplete.MatchesFilters(filters));
    }

    [Theory]
    [InlineData(FilterMatch.Match, false)] // exclude hides only on a decisive match
    [InlineData(FilterMatch.Unknown, true)] // exclude must NOT hide on Unknown
    [InlineData(FilterMatch.NoMatch, true)]
    public void MatchesFilters_ExcludePolarity(FilterMatch state, bool expectedVisible)
    {
        var filters = new[] { UserDataFilter("UserData[\"Foo\"] == \"x\"", isExcluded: true) };
        var @event = EventWith(Value("Foo", state, "x"));

        Assert.Equal(expectedVisible, @event.MatchesFilters(filters));
    }

    [Theory]
    [InlineData(FilterMatch.Match, true)]
    [InlineData(FilterMatch.Unknown, true)] // include keeps a truncated potential match visible
    [InlineData(FilterMatch.NoMatch, false)]
    public void MatchesFilters_IncludePolarity(FilterMatch state, bool expectedVisible)
    {
        var filters = new[] { UserDataFilter("UserData[\"Foo\"] == \"x\"", isExcluded: false) };
        var @event = EventWith(Value("Foo", state, "x"));

        Assert.Equal(expectedVisible, @event.MatchesFilters(filters));
    }

    [Fact]
    public void MatchesFilters_TwoFilters_EachEvaluatesIndependently()
    {
        var filters = new[]
        {
            UserDataFilter("UserData[\"A\"] == \"x\"", isExcluded: false),
            UserDataFilter("UserData[\"B\"] == \"y\"", isExcluded: false)
        };

        // Only the second include filter matches its own stored field -> included.
        Assert.True(
            EventWith(Value("A", FilterMatch.NoMatch, "x"), Value("B", FilterMatch.Match, "y")).MatchesFilters(filters));

        // Neither include filter matches its stored field -> hidden.
        Assert.False(
            EventWith(Value("A", FilterMatch.NoMatch, "x"), Value("B", FilterMatch.NoMatch, "y")).MatchesFilters(filters));
    }

    private static ResolvedEvent EventWith(params UserDataField[] fields) =>
        new("TestLog", LogPathType.Channel) { UserData = [.. fields] };

    private static SavedFilter UserDataFilter(string expression, bool isExcluded = false)
    {
        var filter = SavedFilter.TryCreate(expression, isExcluded: isExcluded);
        Assert.NotNull(filter);

        return filter;
    }

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
