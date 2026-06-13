// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Runtime.LogTable;
using System.Globalization;
using System.Security.Principal;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class ResolvedEventGroupKeyTests
{
    [Fact]
    public void For_ActivityId_UsesInvariantDFormat()
    {
        var id = Guid.NewGuid();
        var evt = FilterEventBuilder.CreateTestEvent(activityId: id);

        Assert.Equal(
            id.ToString("D", CultureInfo.InvariantCulture),
            ResolvedEventGroupKey.For(ColumnName.ActivityId, evt));
    }

    [Fact]
    public void For_ComputerName_UsesComputerNameValue()
    {
        var evt = FilterEventBuilder.CreateTestEvent(computerName: "HOST-01");

        Assert.Equal("HOST-01", ResolvedEventGroupKey.For(ColumnName.ComputerName, evt));
    }

    [Fact]
    public void For_DateAndTime_UsesInvariantTicks()
    {
        var time = new DateTime(2024, 6, 1, 8, 30, 0, DateTimeKind.Utc);
        var evt = FilterEventBuilder.CreateTestEvent(timeCreated: time);

        Assert.Equal(
            time.Ticks.ToString(CultureInfo.InvariantCulture),
            ResolvedEventGroupKey.For(ColumnName.DateAndTime, evt));
    }

    [Fact]
    public void For_EventId_UsesInvariantNumber()
    {
        var evt = FilterEventBuilder.CreateTestEvent(id: 4242);

        Assert.Equal("4242", ResolvedEventGroupKey.For(ColumnName.EventId, evt));
    }

    [Fact]
    public void For_Keywords_UsesDisplayName()
    {
        var evt = FilterEventBuilder.CreateTestEvent(keywords: ["Audit", "Classic"]);

        Assert.Equal("Audit, Classic", ResolvedEventGroupKey.For(ColumnName.Keywords, evt));
    }

    [Fact]
    public void For_Level_UsesLevelValue()
    {
        var evt = FilterEventBuilder.CreateTestEvent(level: "Warning");

        Assert.Equal("Warning", ResolvedEventGroupKey.For(ColumnName.Level, evt));
    }

    [Fact]
    public void For_Log_UsesLogName()
    {
        var evt = FilterEventBuilder.CreateTestEvent(logName: "Security");

        Assert.Equal("Security", ResolvedEventGroupKey.For(ColumnName.Log, evt));
    }

    [Fact]
    public void For_NullActivityId_IsEmptyBucket()
    {
        var evt = FilterEventBuilder.CreateTestEvent(activityId: null);

        Assert.Equal(string.Empty, ResolvedEventGroupKey.For(ColumnName.ActivityId, evt));
    }

    [Fact]
    public void For_NullComputerName_IsEmptyBucket()
    {
        var evt = FilterEventBuilder.CreateTestEvent(computerName: null!);

        Assert.Equal(string.Empty, ResolvedEventGroupKey.For(ColumnName.ComputerName, evt));
    }

    [Fact]
    public void For_NullProcessId_IsEmptyBucket()
    {
        var evt = FilterEventBuilder.CreateTestEvent(processId: null);

        Assert.Equal(string.Empty, ResolvedEventGroupKey.For(ColumnName.ProcessId, evt));
    }

    [Fact]
    public void For_NullRecordId_IsEmptyBucket()
    {
        var evt = FilterEventBuilder.CreateTestEvent(recordId: null);

        Assert.Equal(string.Empty, ResolvedEventGroupKey.For(ColumnName.RecordId, evt));
    }

    [Fact]
    public void For_NullThreadId_IsEmptyBucket()
    {
        var evt = FilterEventBuilder.CreateTestEvent(threadId: null);

        Assert.Equal(string.Empty, ResolvedEventGroupKey.For(ColumnName.ThreadId, evt));
    }

    [Fact]
    public void For_NullUser_IsEmptyBucket()
    {
        var evt = FilterEventBuilder.CreateTestEvent(userId: null);

        Assert.Equal(string.Empty, ResolvedEventGroupKey.For(ColumnName.User, evt));
    }

    [Fact]
    public void For_ProcessId_UsesInvariantNumber()
    {
        var evt = FilterEventBuilder.CreateTestEvent(processId: 4096);

        Assert.Equal("4096", ResolvedEventGroupKey.For(ColumnName.ProcessId, evt));
    }

    [Fact]
    public void For_RecordId_UsesInvariantNumber()
    {
        var evt = FilterEventBuilder.CreateTestEvent(recordId: 4096);

        Assert.Equal("4096", ResolvedEventGroupKey.For(ColumnName.RecordId, evt));
    }

    [Fact]
    public void For_Source_UsesSourceValue()
    {
        var evt = FilterEventBuilder.CreateTestEvent(source: "Kernel-Power");

        Assert.Equal("Kernel-Power", ResolvedEventGroupKey.For(ColumnName.Source, evt));
    }

    [Fact]
    public void For_TaskCategory_UsesTaskCategoryValue()
    {
        var evt = FilterEventBuilder.CreateTestEvent(taskCategory: "Logon");

        Assert.Equal("Logon", ResolvedEventGroupKey.For(ColumnName.TaskCategory, evt));
    }

    [Fact]
    public void For_ThreadId_UsesInvariantNumber()
    {
        var evt = FilterEventBuilder.CreateTestEvent(threadId: 88);

        Assert.Equal("88", ResolvedEventGroupKey.For(ColumnName.ThreadId, evt));
    }

    [Fact]
    public void For_User_UsesSidValue()
    {
        var sid = new SecurityIdentifier("S-1-5-18");
        var evt = FilterEventBuilder.CreateTestEvent(userId: sid);

        Assert.Equal(sid.Value, ResolvedEventGroupKey.For(ColumnName.User, evt));
    }
}
