// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using System.Security.Principal;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class EventTableColumnFormatterTests
{
    private static readonly ResolvedEvent s_event = new("Server\\Security.evtx", LogPathType.File)
    {
        RecordId = 42,
        Level = "Information",
        TimeCreated = new DateTime(2026, 6, 18, 6, 57, 20, DateTimeKind.Utc),
        Id = 4624,
        Source = "Microsoft-Windows-Security-Auditing",
        TaskCategory = "Logon",
        ComputerName = "DC01",
        ProcessId = 1234,
        ThreadId = 56,
        Keywords = ["Audit Success"],
        UserId = new SecurityIdentifier("S-1-5-18")
    };
    private static readonly TimeZoneInfo s_plusTwo =
        TimeZoneInfo.CreateCustomTimeZone("UnitTest+2", TimeSpan.FromHours(2), "UnitTest+2", "UnitTest+2");

    [Fact]
    public void GetCellText_DateAndTime_WithFormat_UsesInvariantIsoInTimeZone()
    {
        string actual = EventTableColumnFormatter.GetCellText(
            s_event, ColumnName.DateAndTime, s_plusTwo, "yyyy-MM-dd HH:mm:ss");

        Assert.Equal("2026-06-18 08:57:20", actual);
    }

    [Fact]
    public void GetCellText_DateAndTime_WithoutFormat_MatchesDisplayToString()
    {
        string expected = TimeZoneInfo.ConvertTimeFromUtc(s_event.TimeCreated, s_plusTwo).ToString();

        Assert.Equal(expected, EventTableColumnFormatter.GetCellText(s_event, ColumnName.DateAndTime, s_plusTwo));
    }

    [Fact]
    public void GetCellText_EventId_ReturnsId()
    {
        Assert.Equal("4624", EventTableColumnFormatter.GetCellText(s_event, ColumnName.EventId, s_plusTwo));
    }

    [Fact]
    public void GetCellText_Keywords_ReturnsDisplayName()
    {
        Assert.Equal("Audit Success", EventTableColumnFormatter.GetCellText(s_event, ColumnName.Keywords, s_plusTwo));
    }

    [Fact]
    public void GetCellText_Log_ReturnsLeafAfterLastBackslash()
    {
        Assert.Equal("Security.evtx", EventTableColumnFormatter.GetCellText(s_event, ColumnName.Log, s_plusTwo));
    }

    [Fact]
    public void GetCellText_MissingNullableValues_ReturnsEmpty()
    {
        var bare = new ResolvedEvent("Log", LogPathType.Channel);

        Assert.Equal(string.Empty, EventTableColumnFormatter.GetCellText(bare, ColumnName.RecordId, s_plusTwo));
        Assert.Equal(string.Empty, EventTableColumnFormatter.GetCellText(bare, ColumnName.User, s_plusTwo));
        Assert.Equal(string.Empty, EventTableColumnFormatter.GetCellText(bare, ColumnName.ActivityId, s_plusTwo));
        Assert.Equal(string.Empty, EventTableColumnFormatter.GetCellText(bare, ColumnName.ProcessId, s_plusTwo));
    }

    [Fact]
    public void GetCellText_User_ReturnsSidValue()
    {
        Assert.Equal("S-1-5-18", EventTableColumnFormatter.GetCellText(s_event, ColumnName.User, s_plusTwo));
    }

    [Fact]
    public void GetColumnHeader_DateAndTime_Local_IsPlainLabel()
    {
        Assert.Equal(
            "Date and Time", EventTableColumnFormatter.GetColumnHeader(ColumnName.DateAndTime, TimeZoneInfo.Local));
    }

    [Fact]
    public void GetColumnHeader_DateAndTime_NonLocal_IncludesTimeZoneToken()
    {
        string header = EventTableColumnFormatter.GetColumnHeader(ColumnName.DateAndTime, s_plusTwo);

        Assert.StartsWith("Date and Time ", header);
        Assert.NotEqual("Date and Time", header);
    }

    [Fact]
    public void GetColumnHeader_OtherColumn_UsesEnumMemberDisplayName()
    {
        Assert.Equal("Event ID", EventTableColumnFormatter.GetColumnHeader(ColumnName.EventId, s_plusTwo));
        Assert.Equal("Record ID", EventTableColumnFormatter.GetColumnHeader(ColumnName.RecordId, s_plusTwo));
    }
}
