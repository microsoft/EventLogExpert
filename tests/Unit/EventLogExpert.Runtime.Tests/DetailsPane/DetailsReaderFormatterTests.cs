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

        // Exact copy LINE (not a substring or key-prefix check): the decoded label rides in copy while the glossary
        // description is display-only. A GlossaryTerm leaking into copy would stringify to its member name "LogonType"
        // (not "Explain_"), so an exact-line assert - not DoesNotContain("Explain_") - is what actually catches it.
        Assert.Contains("LogonType: 3 (Network)", copy.Split(Environment.NewLine));
        Assert.DoesNotContain("How the logon was initiated", copy);
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
    public void BuildEventCopyText_UsesInvariantLabelsNotEnumMemberNames()
    {
        // The copy builders render DetailsPropertyLabel through DetailsPropertyText.Invariant; without that routing the
        // enum would stringify to its member name. Pins the divergent labels (member name != display) so a regression
        // to "$"{property.Label}" is caught - the section-order test only exercises the non-divergent "Source".
        ResolvedEvent @event = new ResolvedEvent("Security", LogPathType.Channel) with
        {
            Id = 4624,
            Source = "Contoso",
            LogName = "Security",
            RecordId = 7,
            UserId = new SecurityIdentifier("S-1-5-21-1-2-3-1105"),
            UserDisplayName = @"CONTOSO\alice"
        };

        string copy = DetailsReaderFormatter.BuildEventCopyText(Model(@event));

        Assert.Contains("Date and Time:", copy);
        Assert.Contains("Log Name: Security", copy);
        Assert.Contains("Record ID: 7", copy);
        Assert.Contains("User SID: S-1-5-21-1-2-3-1105", copy);
        Assert.DoesNotContain("DateTime:", copy);
        Assert.DoesNotContain("LogName:", copy);
        Assert.DoesNotContain("RecordId:", copy);
        Assert.DoesNotContain("UserSid:", copy);
    }

    [Fact]
    public void BuildModel_CorrelationIds_RemainInSystemPropertiesAndCopyText()
    {
        var activityId = Guid.NewGuid();
        ResolvedEvent @event = new ResolvedEvent("TestLog", LogPathType.Channel) with { ActivityId = activityId };

        DetailsReaderModel model = Model(@event);

        Assert.Contains(model.SystemProperties, property => property.Label == DetailsPropertyLabel.ActivityId && property.Value == activityId.ToString());
        Assert.Contains(activityId.ToString(), DetailsReaderFormatter.BuildEventCopyText(model));
    }

    [Fact]
    public void BuildModel_DerivedIdentity_RendersUserDisplayNameOnly()
    {
        // Security-audit event: no System <Security UserID>, name derived from EventData. The Subject/Target SID is
        // shown in the EventData section, so no "User SID" row is hoisted here.
        ResolvedEvent @event = new ResolvedEvent("TestLog", LogPathType.Channel) with { UserDisplayName = @"CONTOSO\alice" };

        DetailsProperty user = Assert.Single(Model(@event).SystemProperties, property => property.Label == DetailsPropertyLabel.User);

        Assert.Equal(@"CONTOSO\alice", user.Value);
        Assert.DoesNotContain(Model(@event).SystemProperties, property => property.Label == DetailsPropertyLabel.UserSid);
    }

    [Fact]
    public void BuildModel_EmptyStringValue_IsMutedButCopiesRealValue()
    {
        DetailsField field = Assert.Single(Model(EventDataTestFactory.CreateEventWithData(("SubjectUserName", ""))).EventData);

        Assert.True(field.IsMuted);
        Assert.Equal("(empty)", Assert.Single(field.PreviewLines));
        Assert.Equal(PlaceholderKind.Empty, field.Placeholder);
        Assert.Equal(string.Empty, field.CopyValue);
    }

    [Fact]
    public void BuildModel_FullyPopulatedEvent_EmitsEveryPropertyLabelExactlyOnce()
    {
        // A fully populated event so every conditional row emits; pins the formatter's emissions to the full
        // DetailsPropertyLabel set, so a new enum member with no emission (or an emission dropped from the formatter)
        // fails here instead of escaping the localization guards.
        ResolvedEvent @event = new ResolvedEvent("Security", LogPathType.Channel) with
        {
            Id = 4624,
            Source = "Contoso",
            TimeCreated = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ComputerName = "PC-01",
            LogName = "Security",
            TaskCategory = "Logon",
            Opcode = "Info",
            ResolutionStatus = EventResolutionStatus.NoProvider,
            Keywords = ["Audit Success"],
            RecordId = 1,
            ProcessId = 100,
            ThreadId = 200,
            ActivityId = Guid.NewGuid(),
            RelatedActivityId = Guid.NewGuid(),
            UserDisplayName = @"CONTOSO\alice",
            UserId = new SecurityIdentifier("S-1-5-21-1-2-3-1105")
        };

        DetailsReaderModel model = Model(@event);
        List<DetailsPropertyLabel> emitted = model.Header
            .Concat(model.SystemProperties)
            .Select(property => property.Label)
            .ToList();

        Assert.Equal(
            Enum.GetValues<DetailsPropertyLabel>().OrderBy(label => label).ToList(),
            emitted.OrderBy(label => label).ToList());
        Assert.Equal(emitted.Count, emitted.Distinct().Count());
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
        // Event ID and Level are typed summary fields on the model (there is no DetailsPropertyLabel member for them),
        // so the header grid carries only the always-present Date and Time row for this minimal event.
        ResolvedEvent @event = new ResolvedEvent("TestLog", LogPathType.Channel) with { Id = 4624, Level = "Warning" };

        DetailsReaderModel model = Model(@event);

        Assert.Equal("4624", model.EventId);
        Assert.Equal("Warning", model.Level);
        DetailsProperty header = Assert.Single(model.Header);
        Assert.Equal(DetailsPropertyLabel.DateTime, header.Label);
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
        Assert.Equal(PlaceholderKind.NullValue, field.Placeholder);
    }

    [Fact]
    public void BuildModel_OmitsEmptySystemProperties()
    {
        ResolvedEvent @event = new ResolvedEvent("TestLog", LogPathType.Channel) with { Opcode = "", TaskCategory = "", ProcessId = 42 };

        DetailsReaderModel model = Model(@event);

        Assert.DoesNotContain(model.SystemProperties, property => property.Label == DetailsPropertyLabel.Opcode);
        Assert.Contains(model.SystemProperties, property => property.Label == DetailsPropertyLabel.ProcessId);
    }

    [Fact]
    public void BuildModel_ResolutionStatusRow_CarriesStatusValueWhileOtherRowsDoNot()
    {
        // StatusValue is a display-override hint set ONLY on the resolution-status row; its Value stays the invariant
        // token that copy emits unconditionally. Every other row leaves StatusValue null (the iff), so nothing else
        // can trigger the UI's localized-status branch.
        ResolvedEvent @event = new ResolvedEvent("TestLog", LogPathType.Channel) with
        {
            Source = "Contoso",
            ResolutionStatus = EventResolutionStatus.NoProvider
        };

        DetailsReaderModel model = Model(@event);
        DetailsProperty statusRow = Assert.Single(model.SystemProperties, property => property.Label == DetailsPropertyLabel.ResolutionStatus);

        Assert.Equal(EventResolutionStatus.NoProvider, statusRow.StatusValue);
        Assert.Equal(ResolutionStatusTokens.Format(EventResolutionStatus.NoProvider), statusRow.Value);
        Assert.All(
            model.Header.Concat(model.SystemProperties).Where(property => property.Label != DetailsPropertyLabel.ResolutionStatus),
            property => Assert.Null(property.StatusValue));
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
    public void BuildModel_UnresolvedUser_RendersSidOnceWithoutDuplicateSidRow()
    {
        // A populated but non-well-known SID that resolves to nothing: UserDisplayName == the SID, so the "User SID"
        // row is suppressed to avoid showing the SID twice.
        ResolvedEvent @event = new ResolvedEvent("TestLog", LogPathType.Channel)
            with { UserId = new SecurityIdentifier("S-1-5-21-1-2-3-1105"), UserDisplayName = "S-1-5-21-1-2-3-1105" };

        DetailsProperty user = Assert.Single(Model(@event).SystemProperties, property => property.Label == DetailsPropertyLabel.User);

        Assert.Equal("S-1-5-21-1-2-3-1105", user.Value);
        Assert.DoesNotContain(Model(@event).SystemProperties, property => property.Label == DetailsPropertyLabel.UserSid);
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
    public void BuildModel_WellKnownUser_RendersBothNameAndSid()
    {
        ResolvedEvent @event = new ResolvedEvent("TestLog", LogPathType.Channel)
            with { UserId = new SecurityIdentifier("S-1-5-18"), UserDisplayName = @"NT AUTHORITY\SYSTEM" };

        DetailsProperty user = Assert.Single(Model(@event).SystemProperties, property => property.Label == DetailsPropertyLabel.User);

        DetailsProperty userSid = Assert.Single(Model(@event).SystemProperties, property => property.Label == DetailsPropertyLabel.UserSid);

        Assert.Equal(@"NT AUTHORITY\SYSTEM", user.Value);
        Assert.Equal("S-1-5-18", userSid.Value);
    }

    [Fact]
    public void PreferFullWidth_TrueForExplainedField()
    {
        ResolvedEvent @event = EventDataTestFactory.CreateEventWithData(("LogonType", 3)) with { Source = SecurityAuditing, Id = 4624 };

        DetailsField field = Assert.Single(Model(@event).EventData);

        Assert.Equal(GlossaryTerm.LogonType, field.Explanation);
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
