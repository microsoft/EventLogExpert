// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class ColumnDescriptorsTests
{
    private static readonly ColumnFormatContext Context = new(TimeZoneInfo.Utc);

    [Fact]
    public void EveryColumn_HasAnAccessor()
    {
        // Anti-drift guard: a new ColumnName added without a registry entry makes GetFieldId throw here.
        foreach (ColumnName column in Enum.GetValues<ColumnName>())
        {
            _ = ColumnDescriptors.GetFieldId(column);
        }
    }

    [Fact]
    public void GetCellText_Log_UsesOwningFileShortName() =>
        Assert.Equal("App.evtx", ColumnDescriptors.GetCellText(SampleEvent(), ColumnName.Log, Context));

    [Theory]
    [InlineData(ColumnName.RecordId, "7")]
    [InlineData(ColumnName.Level, "Warning")]
    [InlineData(ColumnName.ComputerName, "TEST-PC")]
    [InlineData(ColumnName.Source, "TestSource")]
    [InlineData(ColumnName.EventId, "4242")]
    [InlineData(ColumnName.TaskCategory, "TestCategory")]
    [InlineData(ColumnName.Keywords, "AuditKeyword")]
    [InlineData(ColumnName.ProcessId, "111")]
    [InlineData(ColumnName.ThreadId, "222")]
    [InlineData(ColumnName.User, @"CONTOSO\alice")]
    public void GetCellText_RendersColumnValue(ColumnName column, string expected) =>
        Assert.Equal(expected, ColumnDescriptors.GetCellText(SampleEvent(), column, Context));

    [Fact]
    public void GetFieldId_OutOfRangeColumn_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ColumnDescriptors.GetFieldId((ColumnName)999));

    [Theory]
    [InlineData(ColumnName.RecordId, EventFieldId.RecordId)]
    [InlineData(ColumnName.Level, EventFieldId.Level)]
    [InlineData(ColumnName.DateAndTime, EventFieldId.TimeCreated)]
    [InlineData(ColumnName.ActivityId, EventFieldId.ActivityId)]
    [InlineData(ColumnName.Log, EventFieldId.LogName)]
    [InlineData(ColumnName.ComputerName, EventFieldId.ComputerName)]
    [InlineData(ColumnName.Source, EventFieldId.Source)]
    [InlineData(ColumnName.EventId, EventFieldId.Id)]
    [InlineData(ColumnName.TaskCategory, EventFieldId.TaskCategory)]
    [InlineData(ColumnName.Keywords, EventFieldId.KeywordsDisplay)]
    [InlineData(ColumnName.ProcessId, EventFieldId.ProcessId)]
    [InlineData(ColumnName.ThreadId, EventFieldId.ThreadId)]
    [InlineData(ColumnName.User, EventFieldId.UserDisplayName)]
    public void GetFieldId_ReturnsProjectedField(ColumnName column, EventFieldId expected) =>
        Assert.Equal(expected, ColumnDescriptors.GetFieldId(column));

    [Theory]
    [InlineData(ColumnName.EventId, EventProperty.Id)]
    [InlineData(ColumnName.ActivityId, EventProperty.ActivityId)]
    [InlineData(ColumnName.Level, EventProperty.Level)]
    [InlineData(ColumnName.Keywords, EventProperty.Keywords)]
    [InlineData(ColumnName.Source, EventProperty.Source)]
    [InlineData(ColumnName.TaskCategory, EventProperty.TaskCategory)]
    [InlineData(ColumnName.ProcessId, EventProperty.ProcessId)]
    [InlineData(ColumnName.ThreadId, EventProperty.ThreadId)]
    [InlineData(ColumnName.User, EventProperty.UserDisplayName)]
    public void GetFilterProperty_FilterableColumn_ReturnsProperty(ColumnName column, EventProperty expected) =>
        Assert.Equal(expected, ColumnDescriptors.GetFilterProperty(column));

    [Theory]
    [InlineData(ColumnName.RecordId)]
    [InlineData(ColumnName.DateAndTime)]
    [InlineData(ColumnName.Log)]
    [InlineData(ColumnName.ComputerName)]
    public void GetFilterProperty_NonFilterableColumn_ReturnsNull(ColumnName column) =>
        Assert.Null(ColumnDescriptors.GetFilterProperty(column));

    [Fact]
    public void GetGroupText_Log_UsesChannelLogNameNotOwningFile()
    {
        ResolvedEvent sample = SampleEvent();

        // The Log group header is keyed on the channel LogName, which must differ from the cell's owning-file short name.
        Assert.Equal("Application", ColumnDescriptors.GetGroupText(sample, ColumnName.Log, Context));
        Assert.NotEqual(
            ColumnDescriptors.GetCellText(sample, ColumnName.Log, Context),
            ColumnDescriptors.GetGroupText(sample, ColumnName.Log, Context));
    }

    [Theory]
    [InlineData(ColumnName.Level)]
    [InlineData(ColumnName.Source)]
    [InlineData(ColumnName.EventId)]
    public void GetGroupText_NonLogColumn_MatchesCellText(ColumnName column)
    {
        ResolvedEvent sample = SampleEvent();

        Assert.Equal(
            ColumnDescriptors.GetCellText(sample, column, Context),
            ColumnDescriptors.GetGroupText(sample, column, Context));
    }

    private static ResolvedEvent SampleEvent() =>
        new(@"C:\logs\App.evtx", LogPathType.File)
        {
            RecordId = 7,
            Id = 4242,
            ActivityId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            Level = "Warning",
            LogName = "Application",
            ComputerName = "TEST-PC",
            Source = "TestSource",
            TaskCategory = "TestCategory",
            Keywords = ["AuditKeyword"],
            ProcessId = 111,
            ThreadId = 222,
            UserDisplayName = @"CONTOSO\alice",
            TimeCreated = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc)
        };
}
