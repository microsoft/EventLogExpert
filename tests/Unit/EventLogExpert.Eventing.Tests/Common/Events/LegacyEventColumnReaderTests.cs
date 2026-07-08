// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Structured;
using EventLogExpert.Eventing.TestUtils;
using System.Collections.Immutable;
using System.Security.Principal;

namespace EventLogExpert.Eventing.Tests.Common.Events;

public sealed class LegacyEventColumnReaderTests
{
    private static readonly EventLogId s_logId = EventLogId.Create();

    [Fact]
    public void GetField_LocatorFromDifferentGeneration_Throws()
    {
        var reader = CreateReader(new ResolvedEvent("live", LogPathType.Channel));
        var staleLocator = new EventLocator(s_logId, 999, 0);

        Assert.Throws<ArgumentException>(() => reader.GetField(staleLocator, EventFieldId.Id));
    }

    [Fact]
    public void GetField_LocatorFromDifferentLog_Throws()
    {
        var reader = CreateReader(new ResolvedEvent("live", LogPathType.Channel));
        var foreignLocator = new EventLocator(EventLogId.Create(), 3, 0);

        Assert.Throws<ArgumentException>(() => reader.GetField(foreignLocator, EventFieldId.Id));
    }

    [Fact]
    public void GetField_NullRecordId_IsNullKind()
    {
        var reader = CreateReader(new ResolvedEvent("live", LogPathType.Channel) { RecordId = null });

        Assert.Equal(EventFieldValueKind.Null, reader.GetField(reader.LocatorAt(0), EventFieldId.RecordId).Kind);
    }

    [Fact]
    public void GetField_ScalarFields_MatchEvent()
    {
        var reader = CreateReader(new ResolvedEvent("live", LogPathType.Channel)
        {
            Id = 4624,
            Level = "Information",
            Source = "Microsoft-Windows-Security-Auditing",
            RecordId = 7
        });

        EventLocator locator = reader.LocatorAt(0);

        Assert.True(reader.GetField(locator, EventFieldId.Id).TryGetInt64(out long id));
        Assert.Equal(4624L, id);
        Assert.Equal("Information", reader.GetField(locator, EventFieldId.Level).AsString());
        Assert.Equal("Microsoft-Windows-Security-Auditing", reader.GetField(locator, EventFieldId.Source).AsString());
        Assert.True(reader.GetField(locator, EventFieldId.RecordId).TryGetInt64(out long recordId));
        Assert.Equal(7L, recordId);
    }

    [Fact]
    public void GetField_UserId_IsSid()
    {
        var reader = CreateReader(new ResolvedEvent("live", LogPathType.Channel) { UserId = new SecurityIdentifier("S-1-5-18") });

        Assert.Equal("S-1-5-18", reader.GetField(reader.LocatorAt(0), EventFieldId.UserId).AsString());
    }

    [Fact]
    public void GetUserData_AbsentField_IsAbsent()
    {
        var reader = CreateReader(new ResolvedEvent("live", LogPathType.Channel)
        {
            UserData = ImmutableArray.Create(new UserDataField("Result/@value", ImmutableArray.Create("ok"), IsTruncated: false))
        });

        Assert.True(reader.GetUserData(reader.LocatorAt(0), "Missing/@value").IsAbsent);
    }

    [Fact]
    public void GetUserData_ReturnsStoredField()
    {
        var reader = CreateReader(new ResolvedEvent("live", LogPathType.Channel)
        {
            UserData = ImmutableArray.Create(new UserDataField("Result/@value", ImmutableArray.Create("ok"), IsTruncated: false))
        });

        StructuredFieldResult result = reader.GetUserData(reader.LocatorAt(0), "Result/@value");

        Assert.False(result.IsAbsent);
        Assert.Equal(["ok"], result.PresentValues.ToArray());
    }

    [Fact]
    public void LocatorAt_CarriesReaderLogAndGeneration()
    {
        var reader = CreateReader(new ResolvedEvent("live", LogPathType.Channel));

        Assert.Equal(new EventLocator(s_logId, 3, 0), reader.LocatorAt(0));
    }

    [Fact]
    public void TryGetEventData_AbsentField_ReturnsFalse()
    {
        var reader = CreateReader(new ResolvedEvent("live", LogPathType.Channel).WithEventData(("TargetUserName", "alice")));

        Assert.False(reader.TryGetEventData(reader.LocatorAt(0), "NonExistent", out _));
    }

    [Fact]
    public void TryGetEventData_ReturnsNamedField()
    {
        var reader = CreateReader(new ResolvedEvent("live", LogPathType.Channel).WithEventData(("TargetUserName", "alice")));

        Assert.True(reader.TryGetEventData(reader.LocatorAt(0), "TargetUserName", out EventFieldValue value));
        Assert.Equal("alice", value.AsString());
    }

    private static LegacyEventColumnReader CreateReader(params ResolvedEvent[] events) =>
        new(s_logId, generation: 3, contentVersion: 1, events);
}
