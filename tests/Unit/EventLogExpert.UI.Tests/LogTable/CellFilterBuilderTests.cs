// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.UI.LogTable;
using System.Security.Principal;

namespace EventLogExpert.UI.Tests.LogTable;

public sealed class CellFilterBuilderTests
{
    private static readonly Guid s_activityId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly SecurityIdentifier s_userSid = new("S-1-5-21-1111111111-2222222222-3333333333-1001");

    [Theory]
    [InlineData(ColumnName.EventId, EventProperty.Id)]
    [InlineData(ColumnName.ActivityId, EventProperty.ActivityId)]
    [InlineData(ColumnName.Level, EventProperty.Level)]
    [InlineData(ColumnName.Keywords, EventProperty.Keywords)]
    [InlineData(ColumnName.Source, EventProperty.Source)]
    [InlineData(ColumnName.TaskCategory, EventProperty.TaskCategory)]
    [InlineData(ColumnName.ProcessId, EventProperty.ProcessId)]
    [InlineData(ColumnName.ThreadId, EventProperty.ThreadId)]
    [InlineData(ColumnName.User, EventProperty.UserId)]
    public void MapColumn_SupportedColumn_ReturnsProperty(ColumnName column, EventProperty expected) =>
        Assert.Equal(expected, CellFilterBuilder.MapColumn(column));

    [Theory]
    [InlineData(ColumnName.DateAndTime)]
    [InlineData(ColumnName.Log)]
    [InlineData(ColumnName.ComputerName)]
    public void MapColumn_UnsupportedColumn_ReturnsNull(ColumnName column) =>
        Assert.Null(CellFilterBuilder.MapColumn(column));

    [Fact]
    public void TryBuild_ActivityId_MatchesSameGuidButNotDifferent()
    {
        var sourceEvent = FullEvent();
        var otherEvent = FullEvent() with { ActivityId = Guid.NewGuid() };

        Assert.True(CellFilterBuilder.TryBuild(sourceEvent, EventProperty.ActivityId, exclude: false, out var filter));
        Assert.True(filter.Compiled!.Predicate(sourceEvent));
        Assert.False(filter.Compiled!.Predicate(otherEvent));
    }

    [Fact]
    public void TryBuild_Exclude_SetsIsExcludedAndPredicateStillMatchesSource()
    {
        var sourceEvent = FullEvent();

        Assert.True(CellFilterBuilder.TryBuild(sourceEvent, EventProperty.Source, exclude: true, out var filter));
        Assert.True(filter.IsExcluded);
        Assert.True(filter.Compiled!.Predicate(sourceEvent));
    }

    [Theory]
    [InlineData(EventProperty.Id)]
    [InlineData(EventProperty.ActivityId)]
    [InlineData(EventProperty.Level)]
    [InlineData(EventProperty.Keywords)]
    [InlineData(EventProperty.Source)]
    [InlineData(EventProperty.TaskCategory)]
    [InlineData(EventProperty.ProcessId)]
    [InlineData(EventProperty.ThreadId)]
    [InlineData(EventProperty.UserId)]
    public void TryBuild_IncludeFilter_MatchesSourceEvent(EventProperty property)
    {
        var sourceEvent = FullEvent();

        Assert.True(CellFilterBuilder.TryBuild(sourceEvent, property, exclude: false, out var filter));
        Assert.False(filter.IsExcluded);
        Assert.NotNull(filter.Compiled);
        Assert.True(filter.Compiled!.Predicate(sourceEvent));
    }

    [Fact]
    public void TryBuild_MultipleKeywords_MatchesAnyOfThem()
    {
        var sourceEvent = FullEvent() with { Keywords = ["Classic", "Audit Success"] };
        var sharesOneKeyword = FullEvent() with { Keywords = ["Audit Success"] };
        var sharesNoKeyword = FullEvent() with { Keywords = ["Audit Failure"] };

        Assert.True(CellFilterBuilder.TryBuild(sourceEvent, EventProperty.Keywords, exclude: false, out var filter));
        Assert.True(filter.Compiled!.Predicate(sourceEvent));
        Assert.True(filter.Compiled!.Predicate(sharesOneKeyword));
        Assert.False(filter.Compiled!.Predicate(sharesNoKeyword));
    }

    [Fact]
    public void TryBuild_ProcessId_DoesNotMatchEventWithDifferentValue()
    {
        var sourceEvent = FullEvent() with { ProcessId = 111 };
        var otherEvent = FullEvent() with { ProcessId = 999 };

        Assert.True(CellFilterBuilder.TryBuild(sourceEvent, EventProperty.ProcessId, exclude: false, out var filter));
        Assert.True(filter.Compiled!.Predicate(sourceEvent));
        Assert.False(filter.Compiled!.Predicate(otherEvent));
    }

    [Fact]
    public void TryBuild_SingleKeyword_MatchesThatKeyword()
    {
        var sourceEvent = FullEvent() with { Keywords = ["Audit Success"] };

        Assert.True(CellFilterBuilder.TryBuild(sourceEvent, EventProperty.Keywords, exclude: false, out var filter));
        Assert.True(filter.Compiled!.Predicate(sourceEvent));
    }

    [Fact]
    public void TryBuild_Source_ProducesComparisonTextReadableInAdvancedEditor()
    {
        var sourceEvent = FullEvent() with { Source = "Service Control Manager" };

        Assert.True(CellFilterBuilder.TryBuild(sourceEvent, EventProperty.Source, exclude: false, out var filter));
        Assert.Equal("Source == \"Service Control Manager\"", filter.ComparisonText);
    }

    [Fact]
    public void TryBuild_UserId_MatchesSameSidButNotDifferentSid()
    {
        var sourceEvent = FullEvent();
        var otherEvent = FullEvent() with { UserId = new SecurityIdentifier("S-1-5-18") };

        Assert.True(CellFilterBuilder.TryBuild(sourceEvent, EventProperty.UserId, exclude: false, out var filter));
        Assert.True(filter.Compiled!.Predicate(sourceEvent));
        Assert.False(filter.Compiled!.Predicate(otherEvent));
    }

    [Theory]
    [InlineData(EventProperty.ActivityId)]
    [InlineData(EventProperty.UserId)]
    [InlineData(EventProperty.ProcessId)]
    [InlineData(EventProperty.ThreadId)]
    [InlineData(EventProperty.Level)]
    [InlineData(EventProperty.Keywords)]
    public void TryBuild_WhenCellHasNoValue_ReturnsFalse(EventProperty property)
    {
        var emptyEvent = EmptyFor(property);

        Assert.False(CellFilterBuilder.TryBuild(emptyEvent, property, exclude: false, out _));
    }

    [Theory]
    [InlineData(EventProperty.ProcessId)]
    [InlineData(EventProperty.ThreadId)]
    [InlineData(EventProperty.UserId)]
    public void TryGetDisplayValue_PreviouslyUnmappedProperties_ReturnNonEmptyValue(EventProperty property)
    {
        Assert.True(CellFilterBuilder.TryGetDisplayValue(FullEvent(), property, out var value));
        Assert.False(string.IsNullOrWhiteSpace(value));
    }

    private static ResolvedEvent EmptyFor(EventProperty property) =>
        property switch
        {
            EventProperty.ActivityId => FullEvent() with { ActivityId = null },
            EventProperty.UserId => FullEvent() with { UserId = null },
            EventProperty.ProcessId => FullEvent() with { ProcessId = null },
            EventProperty.ThreadId => FullEvent() with { ThreadId = null },
            EventProperty.Level => FullEvent() with { Level = string.Empty },
            EventProperty.Keywords => FullEvent() with { Keywords = [] },
            _ => FullEvent()
        };

    private static ResolvedEvent FullEvent() =>
        new("Application", LogPathType.Channel)
        {
            Id = 4242,
            ActivityId = s_activityId,
            Level = "Warning",
            Source = "TestSource",
            TaskCategory = "TestCategory",
            Keywords = ["AuditKeyword"],
            ProcessId = 111,
            ThreadId = 222,
            UserId = s_userSid,
            ComputerName = "TEST-PC",
            Description = "test description"
        };
}
