// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class EventFindMatcherTests
{
    private static readonly ColumnName[] s_allColumns =
    [
        ColumnName.Level, ColumnName.DateAndTime, ColumnName.Source, ColumnName.EventId, ColumnName.ComputerName
    ];
    private static readonly ResolvedEvent s_event = new("Server\\Security.evtx", LogPathType.File)
    {
        RecordId = 42,
        Level = "Information",
        TimeCreated = new DateTime(2026, 6, 18, 6, 57, 20, DateTimeKind.Utc),
        Id = 4624,
        Source = "Microsoft-Windows-Security-Auditing",
        ComputerName = "DC01",
        Description = "An account was successfully logged on."
    };

    [Fact]
    public void ComparisonFor_MapsCaseFlagToOrdinalVariants()
    {
        Assert.Equal(StringComparison.Ordinal, EventFindMatcher.ComparisonFor(caseSensitive: true));
        Assert.Equal(StringComparison.OrdinalIgnoreCase, EventFindMatcher.ComparisonFor(caseSensitive: false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndexOfMatch_EmptyQuery_ReturnsMinusOne(bool wholeWord)
    {
        // Empty query returns -1 in both modes: guards the public helper (whole-word boundary indexing would otherwise read text[-1]).
        Assert.Equal(-1, EventFindMatcher.IndexOfMatch("account", string.Empty, 0, StringComparison.Ordinal, wholeWord));
    }

    [Fact]
    public void IndexOfMatch_Substring_ReturnsEveryOccurrence()
    {
        // Without whole-word, IndexOfMatch returns the sub-token "cat" inside "catalog" (index 4).
        Assert.Equal(4, EventFindMatcher.IndexOfMatch("cat catalog cat", "cat", 1, StringComparison.Ordinal, wholeWord: false));
    }

    [Theory]
    [InlineData("cat catalog cat", "cat", 0, 0)]  // first token is bounded
    [InlineData("cat catalog cat", "cat", 1, 12)] // skips the "cat" inside "catalog", finds the trailing token
    [InlineData("catalog", "cat", 0, -1)]         // only sub-token occurrence -> no whole-word match
    public void IndexOfMatch_WholeWord_ReturnsOnlyWordBoundedOccurrences(string text, string query, int start, int expected)
    {
        Assert.Equal(expected, EventFindMatcher.IndexOfMatch(text, query, start, StringComparison.Ordinal, wholeWord: true));
    }

    [Fact]
    public void RowMatches_CaseInsensitiveByDefault()
    {
        Assert.True(EventFindMatcher.RowMatches(s_event, s_allColumns, TimeZoneInfo.Utc, "security-auditing", caseSensitive: false, wholeWord: false));
    }

    [Fact]
    public void RowMatches_CaseSensitive_RejectsWrongCase_AcceptsExactCase()
    {
        Assert.False(EventFindMatcher.RowMatches(s_event, s_allColumns, TimeZoneInfo.Utc, "security-auditing", caseSensitive: true, wholeWord: false));
        Assert.True(EventFindMatcher.RowMatches(s_event, s_allColumns, TimeZoneInfo.Utc, "Security-Auditing", caseSensitive: true, wholeWord: false));
    }

    [Fact]
    public void RowMatches_EmptyQuery_NeverMatches()
    {
        Assert.False(EventFindMatcher.RowMatches(s_event, s_allColumns, TimeZoneInfo.Utc, string.Empty, caseSensitive: false, wholeWord: false));
    }

    [Fact]
    public void RowMatches_MatchesDescriptionText()
    {
        Assert.True(EventFindMatcher.RowMatches(s_event, s_allColumns, TimeZoneInfo.Utc, "successfully logged on", caseSensitive: false, wholeWord: false));
    }

    [Fact]
    public void RowMatches_MatchesFormattedColumnText()
    {
        Assert.True(EventFindMatcher.RowMatches(s_event, s_allColumns, TimeZoneInfo.Utc, "4624", caseSensitive: false, wholeWord: false));
        Assert.True(EventFindMatcher.RowMatches(s_event, s_allColumns, TimeZoneInfo.Utc, "DC01", caseSensitive: false, wholeWord: false));
    }

    [Fact]
    public void RowMatches_NoMatch_ReturnsFalse()
    {
        Assert.False(EventFindMatcher.RowMatches(s_event, s_allColumns, TimeZoneInfo.Utc, "nonexistent-token", caseSensitive: false, wholeWord: false));
    }

    [Fact]
    public void RowMatches_OnlySearchesEnabledColumns()
    {
        Assert.False(EventFindMatcher.RowMatches(
            s_event, [ColumnName.EventId, ColumnName.ComputerName], TimeZoneInfo.Utc, "Security-Auditing", caseSensitive: false, wholeWord: false));
    }

    [Fact]
    public void RowMatches_WholeWord_MatchesBoundedEventIdAndHyphenSegment()
    {
        Assert.True(EventFindMatcher.RowMatches(s_event, s_allColumns, TimeZoneInfo.Utc, "4624", caseSensitive: false, wholeWord: true));
        Assert.True(EventFindMatcher.RowMatches(s_event, s_allColumns, TimeZoneInfo.Utc, "Security", caseSensitive: false, wholeWord: true));
    }

    [Fact]
    public void RowMatches_WholeWord_QueryWithSeparatorEdge_IsBounded()
    {
        Assert.True(EventFindMatcher.RowMatches(s_event, s_allColumns, TimeZoneInfo.Utc, "-Auditing", caseSensitive: false, wholeWord: true));
    }

    [Fact]
    public void RowMatches_WholeWord_RejectsMidWordSubstring()
    {
        Assert.True(EventFindMatcher.RowMatches(s_event, s_allColumns, TimeZoneInfo.Utc, "count", caseSensitive: false, wholeWord: false));
        Assert.False(EventFindMatcher.RowMatches(s_event, s_allColumns, TimeZoneInfo.Utc, "count", caseSensitive: false, wholeWord: true));
        Assert.True(EventFindMatcher.RowMatches(s_event, s_allColumns, TimeZoneInfo.Utc, "account", caseSensitive: false, wholeWord: true));
    }

    [Fact]
    public void RowMatches_WholeWord_RejectsNumericSubtoken()
    {
        Assert.True(EventFindMatcher.RowMatches(s_event, s_allColumns, TimeZoneInfo.Utc, "462", caseSensitive: false, wholeWord: false));
        Assert.False(EventFindMatcher.RowMatches(s_event, s_allColumns, TimeZoneInfo.Utc, "462", caseSensitive: false, wholeWord: true));
    }
}
