// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Structured;
using EventLogExpert.Eventing.TestUtils;
using EventLogExpert.Runtime.DetailsPane;
using System.Security.Principal;

namespace EventLogExpert.Runtime.Tests.DetailsPane;

public sealed class DetailsReaderFormatterTests
{
    private const string SecurityAuditing = "Microsoft-Windows-Security-Auditing";

    [Fact]
    public void BuildEventCopyText_EmitsEventIdThenLevelThenHeader()
    {
        ResolvedEvent @event = new ResolvedEvent("TestLog", LogPathType.Channel) with { Id = 4624, Level = "Warning", Source = "Contoso" };

        string[] lines = DetailsReaderFormatter.BuildEventCopyText(Model(@event)).Split(Environment.NewLine);

        Assert.Equal("Event ID: 4624", lines[0]);
        Assert.Equal("Level: Warning", lines[1]);
        Assert.Equal("Source: Contoso", lines[2]);
    }

    [Fact]
    public void BuildEventCopyText_ExcludesXml()
    {
        ResolvedEvent @event = EventDataTestFactory.CreateEventWithData(("LogonType", 3)) with { Xml = "<Event>secret</Event>" };

        string copy = DetailsReaderFormatter.BuildEventCopyText(Model(@event));

        Assert.DoesNotContain("<Event", copy);
    }

    [Fact]
    public void BuildEventCopyText_IncludesDecodedLabelExcludesDescriptionProse()
    {
        ResolvedEvent @event = EventDataTestFactory.CreateEventWithData(("LogonType", 3)) with { Source = SecurityAuditing, Id = 4624 };

        string copy = DetailsReaderFormatter.BuildEventCopyText(Model(@event));

        Assert.Contains("LogonType: 3 (Network)", copy);
        Assert.DoesNotContain("How the logon", copy);
    }

    [Fact]
    public void BuildEventCopyText_MultiValueFieldIndentsItems()
    {
        ResolvedEvent @event = EventDataTestFactory.CreateEventWithData(("Privileges", (string[])["SeDebugPrivilege", "SeBackupPrivilege"]));

        string copy = DetailsReaderFormatter.BuildEventCopyText(Model(@event));

        Assert.Contains("Privileges:", copy);
        Assert.Contains("    SeDebugPrivilege", copy);
        Assert.Contains("    SeBackupPrivilege", copy);
    }

    [Fact]
    public void BuildEventCopyText_OmitsLevelLineWhenLevelEmpty()
    {
        ResolvedEvent @event = new ResolvedEvent("TestLog", LogPathType.Channel) with { Id = 4624, Source = "Contoso" };

        string[] lines = DetailsReaderFormatter.BuildEventCopyText(Model(@event)).Split(Environment.NewLine);

        Assert.Equal("Event ID: 4624", lines[0]);
        Assert.Equal("Source: Contoso", lines[1]);
    }

    [Fact]
    public void BuildEventCopyText_SectionsAppearInStableOrder()
    {
        ResolvedEvent @event = EventDataTestFactory.CreateEventWithData(("Field1", "v1")) with
        {
            Id = 4624,
            Level = "Warning",
            Source = "Contoso",
            ProcessId = 42,
            Description = "A message.",
            UserData = [new UserDataField("Config/Setting", ["u1"], false)]
        };

        string copy = DetailsReaderFormatter.BuildEventCopyText(Model(@event));

        int eventId = copy.IndexOf("Event ID:", StringComparison.Ordinal);
        int level = copy.IndexOf("Level:", StringComparison.Ordinal);
        int source = copy.IndexOf("Source:", StringComparison.Ordinal);
        int processId = copy.IndexOf("Process ID:", StringComparison.Ordinal);
        int message = copy.IndexOf("Message:", StringComparison.Ordinal);
        int eventData = copy.IndexOf("Event Data:", StringComparison.Ordinal);
        int userData = copy.IndexOf("User Data:", StringComparison.Ordinal);

        // Pins the full section order (identity -> System -> Message -> Event Data -> User Data), not just the first
        // lines, so moving any later section is caught.
        Assert.True(
            eventId >= 0 && level > eventId && source > level && processId > source &&
            message > processId && eventData > message && userData > eventData,
            $"Unexpected copy section order:{Environment.NewLine}{copy}");
    }

    [Fact]
    public void BuildModel_CorrelationIds_RemainInSystemPropertiesAndCopyText()
    {
        var activityId = Guid.NewGuid();
        ResolvedEvent @event = new ResolvedEvent("TestLog", LogPathType.Channel) with { ActivityId = activityId };

        DetailsReaderModel model = Model(@event);

        Assert.Contains(model.SystemProperties, property => property.Label == "Activity ID" && property.Value == activityId.ToString());
        Assert.Contains(activityId.ToString(), DetailsReaderFormatter.BuildEventCopyText(model));
    }

    [Fact]
    public void BuildModel_EmptyStringValue_IsMutedButCopiesRealValue()
    {
        DetailsField field = Assert.Single(Model(EventDataTestFactory.CreateEventWithData(("SubjectUserName", ""))).EventData);

        Assert.True(field.IsMuted);
        Assert.Equal("(empty)", Assert.Single(field.PreviewLines));
        Assert.Equal(string.Empty, field.CopyValue);
    }

    [Fact]
    public void BuildModel_GeneralArray_RendersOneLinePerItem()
    {
        DetailsField field = Assert.Single(Model(EventDataTestFactory.CreateEventWithData(("Ports", new uint[] { 80, 443 }))).EventData);

        Assert.Equal(new[] { "80", "443" }, field.FullLines);
    }

    [Fact]
    public void BuildModel_HeaderExcludesEventIdAndLevel()
    {
        ResolvedEvent @event = new ResolvedEvent("TestLog", LogPathType.Channel) with { Id = 4624, Level = "Warning" };

        DetailsReaderModel model = Model(@event);

        Assert.DoesNotContain(model.Header, property => property.Label == "Event ID");
        Assert.DoesNotContain(model.Header, property => property.Label == "Level");
    }

    [Fact]
    public void BuildModel_LargeByteArray_TruncatesPreviewKeepsFullHexCopy()
    {
        DetailsField field = Assert.Single(Model(EventDataTestFactory.CreateEventWithData(("Binary", new byte[100]))).EventData);

        Assert.True(field.IsTruncated);
        Assert.Equal(200, field.CopyValue.Length);
        Assert.NotEqual(field.PreviewLines[0], field.FullLines[0]);
    }

    [Fact]
    public void BuildModel_LegacyEventWithNoEventData_HasNoNamedEventData()
    {
        DetailsReaderModel model = Model(new ResolvedEvent("TestLog", LogPathType.Channel));

        Assert.False(model.HasNamedEventData);
        Assert.Empty(model.EventData);
    }

    [Fact]
    public void BuildModel_LongScalar_TruncatesPreviewKeepsFullCopy()
    {
        string longValue = new('a', 600);

        DetailsField field = Assert.Single(Model(EventDataTestFactory.CreateEventWithData(("CommandLine", longValue))).EventData);

        Assert.True(field.IsTruncated);
        Assert.Equal(longValue, field.CopyValue);
        Assert.Equal(longValue, field.FullLines[0]);
    }

    [Fact]
    public void BuildModel_NullValue_IsMuted()
    {
        DetailsField field = Assert.Single(Model(EventDataTestFactory.CreateEventWithData(("SubjectUserSid", null))).EventData);

        Assert.True(field.IsMuted);
        Assert.Equal("(none)", Assert.Single(field.PreviewLines));
    }

    [Fact]
    public void BuildModel_OmitsEmptySystemProperties()
    {
        ResolvedEvent @event = new ResolvedEvent("TestLog", LogPathType.Channel) with { Opcode = "", TaskCategory = "", ProcessId = 42 };

        DetailsReaderModel model = Model(@event);

        Assert.DoesNotContain(model.SystemProperties, property => property.Label == "Opcode");
        Assert.Contains(model.SystemProperties, property => property.Label == "Process ID");
    }

    [Fact]
    public void BuildModel_SchemaMisalignment_HasNoNamedEventDataDespiteNonZeroCount()
    {
        ResolvedEvent @event = EventDataTestFactory.CreateEventWithUnalignedData("a", "b");

        Assert.Equal(2, @event.EventData.Count);

        DetailsReaderModel model = Model(@event);

        Assert.False(model.HasNamedEventData);
        Assert.Empty(model.EventData);
    }

    [Fact]
    public void BuildModel_SetsEventIdLevelAndSeverity()
    {
        ResolvedEvent @event = new ResolvedEvent("TestLog", LogPathType.Channel) with { Id = 4624, Level = "Warning" };

        DetailsReaderModel model = Model(@event);

        Assert.Equal("4624", model.EventId);
        Assert.Equal("Warning", model.Level);
        Assert.Equal(SeverityLevel.Warning, model.Severity);
    }

    [Fact]
    public void BuildModel_StringArray_PreservesEmbeddedCommasOnePerLine()
    {
        DetailsField field = Assert.Single(Model(EventDataTestFactory.CreateEventWithData(("Groups", (string[])["a,b", "c"]))).EventData);

        Assert.Equal(new[] { "a,b", "c" }, field.FullLines);
        Assert.Equal("a,b\nc", field.CopyValue);
    }

    [Fact]
    public void BuildModel_StructuredTokenFields_AreMonospaced()
    {
        DetailsReaderModel model = Model(EventDataTestFactory.CreateEventWithData(
            ("Id", Guid.NewGuid()),
            ("Sid", new SecurityIdentifier("S-1-5-18")),
            ("Blob", new byte[] { 1, 2 }),
            ("Name", "plain")));

        Assert.True(model.EventData[0].IsMonospace);
        Assert.True(model.EventData[1].IsMonospace);
        Assert.True(model.EventData[2].IsMonospace);
        Assert.False(model.EventData[3].IsMonospace);
    }

    [Fact]
    public void BuildModel_SyntheticPercentName_UsesParameterLabel()
    {
        // A Windows-synthesized "%1" placeholder (e.g. CAPI2 4192) surfaces as "Parameter 1", matching Event Viewer.
        DetailsField field = Assert.Single(Model(EventDataTestFactory.CreateEventWithData(("%1", "MsSense.exe"))).EventData);

        Assert.Equal("Parameter 1", field.Label);
    }

    [Theory]
    [InlineData("")]
    [InlineData("warning")]
    [InlineData("Custom")]
    public void BuildModel_UnknownOrLowercaseLevel_HasNullSeverity(string level)
    {
        ResolvedEvent @event = new ResolvedEvent("TestLog", LogPathType.Channel) with { Level = level };

        Assert.Null(Model(@event).Severity);
    }

    [Fact]
    public void BuildModel_UnnamedField_UsesPositionalLabel()
    {
        DetailsField field = Assert.Single(Model(EventDataTestFactory.CreateEventWithData(("", "value"))).EventData);

        Assert.Equal("[0]", field.Label);
    }

    [Fact]
    public void BuildModel_UserData_RendersPathAndFlagsIncompleteExtraction()
    {
        ResolvedEvent @event = new ResolvedEvent("TestLog", LogPathType.Channel) with
        {
            UserData = [new UserDataField("Config/Setting", ["v1"], false)],
            UserDataIncomplete = true
        };

        DetailsReaderModel model = Model(@event);
        DetailsField field = Assert.Single(model.UserData);

        Assert.True(model.UserDataIncomplete);
        Assert.Equal("Config/Setting", field.Label);
        Assert.Equal("v1", Assert.Single(field.FullLines));
    }

    [Fact]
    public void BuildModel_UserId_RendersRawSid()
    {
        ResolvedEvent @event = new ResolvedEvent("TestLog", LogPathType.Channel) with { UserId = new SecurityIdentifier("S-1-5-18") };

        DetailsProperty user = Assert.Single(Model(@event).SystemProperties, property => property.Label == "User");

        Assert.Equal("S-1-5-18", user.Value);
    }

    [Fact]
    public void PreferFullWidth_TrueForExplainedField()
    {
        ResolvedEvent @event = EventDataTestFactory.CreateEventWithData(("LogonType", 3)) with { Source = SecurityAuditing, Id = 4624 };

        DetailsField field = Assert.Single(Model(@event).EventData);

        Assert.NotNull(field.Description);
        Assert.True(field.PreferFullWidth);
    }

    [Fact]
    public void PreferFullWidth_TrueForMultiItemArray_FalseForSingleElementArray()
    {
        DetailsField multi = Assert.Single(Model(EventDataTestFactory.CreateEventWithData(("Groups", (string[])["a", "b"]))).EventData);
        DetailsField single = Assert.Single(Model(EventDataTestFactory.CreateEventWithData(("Groups", (string[])["only"]))).EventData);

        Assert.True(multi.PreferFullWidth);
        Assert.False(single.PreferFullWidth);
    }

    [Fact]
    public void PreferFullWidth_TrueForTruncatedScalar_FalseForShortScalar()
    {
        DetailsField longField = Assert.Single(Model(EventDataTestFactory.CreateEventWithData(("CommandLine", new string('a', 600)))).EventData);
        DetailsField shortField = Assert.Single(Model(EventDataTestFactory.CreateEventWithData(("Name", "plain"))).EventData);

        Assert.True(longField.PreferFullWidth);
        Assert.False(shortField.PreferFullWidth);
    }

    private static DetailsReaderModel Model(ResolvedEvent @event) => DetailsReaderFormatter.BuildModel(@event, TimeZoneInfo.Utc);
}
