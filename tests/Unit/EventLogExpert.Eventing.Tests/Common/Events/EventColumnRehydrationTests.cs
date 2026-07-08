// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Eventing.Structured;
using EventLogExpert.Eventing.TestUtils;
using System.Collections.Immutable;
using System.Security;
using System.Security.Principal;

namespace EventLogExpert.Eventing.Tests.Common.Events;

public sealed class EventColumnRehydrationTests
{
    private const int ChunkSize = 4096;

    [Fact]
    public void GetDetail_PendingRow_ReturnsSamePendingEvent()
    {
        ResolvedEvent pending = BuildRichEvent();
        EventColumnStore store = EventColumnStore.Build([], generation: 1, contentVersion: 1).Append([pending]);

        Assert.True(store.IsPending(0));
        Assert.Same(pending, store.GetDetail(0));
    }

    [Fact]
    public void GetDetail_SameEventPendingThenSealed_YieldsIdenticalObservableOutput()
    {
        ResolvedEvent target = BuildRichEvent();

        EventColumnStore pendingStore = EventColumnStore.Build([], generation: 1, contentVersion: 1).Append([target]);
        Assert.True(pendingStore.IsPending(0));
        ResolvedEvent pendingDetail = pendingStore.GetDetail(0);

        // Cross the chunk threshold so global index 0 seals into the columnar representation.
        ResolvedEvent[] filler = new ResolvedEvent[ChunkSize - 1];
        for (int i = 0; i < filler.Length; i++) { filler[i] = new ResolvedEvent("live", LogPathType.Channel) { Id = i + 1 }; }

        EventColumnStore sealedStore = pendingStore.Append(filler);
        Assert.False(sealedStore.IsPending(0));
        ResolvedEvent sealedDetail = sealedStore.GetDetail(0);

        AssertFunctionalParity(pendingDetail, sealedDetail);
    }

    [Fact]
    public void GetDetail_SealedAbsentNullablesAndUserId_MatchesSourceObservable()
    {
        ResolvedEvent source = new("live", LogPathType.Channel)
        {
            Id = 7,
            TimeCreated = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Level = "Error"
        };

        ResolvedEvent detail = SealSingle(source);

        Assert.Null(detail.RecordId);
        Assert.Null(detail.ActivityId);
        Assert.Null(detail.ProcessId);
        Assert.Null(detail.ThreadId);
        Assert.Null(detail.UserId);
        Assert.Empty(detail.Keywords);
        Assert.Equal(string.Empty, detail.KeywordsDisplayName);
        AssertFunctionalParity(source, detail);
    }

    [Fact]
    public void GetDetail_SealedEventDataArraysAndBytes_MatchElementsAndHex()
    {
        ResolvedEvent source = EventWithProperties(
            ("bytes", (EventProperty)(byte[])[0xDE, 0xAD, 0xBE, 0xEF]),
            ("u16", EventProperty.FromReference(new ushort[] { 7, 8, 9 })),
            ("u32", EventProperty.FromReference(new uint[] { 10, 11 })),
            ("i32", EventProperty.FromReference(new[] { -1, -2 })),
            ("names", (EventProperty)(string[])["alice", "bob", "carol"]));

        ResolvedEvent detail = SealSingle(source);
        EventDataView view = detail.EventData;

        // Bytes: Convert.ToHexString is injective, so AsString parity is an exact element check.
        Assert.True(view.TryGetValue("bytes", out EventFieldValue bytes));
        Assert.Equal(EventFieldValueKind.Bytes, bytes.Kind);
        Assert.Equal("DEADBEEF", bytes.AsString());

        // StringArray: element sequence, not just the joined form.
        Assert.True(view.TryGetValue("names", out EventFieldValue names));
        Assert.True(names.TryGetStringArray(out string[]? nameValues));
        Assert.Equal(["alice", "bob", "carol"], nameValues);

        // Numeric arrays: elements survive the byte round-trip with their exact element type.
        Assert.Equal(new ushort[] { 7, 8, 9 }, (ushort[])detail.EventDataValues[1].Reference!);
        Assert.Equal(new uint[] { 10, 11 }, (uint[])detail.EventDataValues[2].Reference!);
        Assert.Equal(new[] { -1, -2 }, (int[])detail.EventDataValues[3].Reference!);

        AssertFunctionalParity(source, detail);
    }

    [Fact]
    public void GetDetail_SealedEventDataEveryStoredFieldKind_MatchesSourceObservable()
    {
        ResolvedEvent source = EventWithProperties(
            ("f00", (sbyte)-5),
            ("f01", (byte)200),
            ("f02", (short)-300),
            ("f03", (ushort)400),
            ("f04", -70000),
            ("f05", 80000u),
            ("f06", -5_000_000_000L),
            ("f07", 9_000_000_000UL),
            ("f08", 3.5f),
            ("f09", 2.5d),
            ("f10", true),
            ("f11", new DateTime(2022, 3, 4, 5, 6, 7, DateTimeKind.Utc)),
            ("f12", (nuint)77),
            ("f13", (EventProperty)"hello"),
            ("f14", (EventProperty)new SecurityIdentifier("S-1-5-32-544")),
            ("f15", Guid.Parse("22222222-2222-2222-2222-222222222222")),
            ("f16", (EventProperty)(byte[])[1, 2, 3, 4]),
            ("f17", EventProperty.FromReference(new ushort[] { 7, 8, 9 })),
            ("f18", EventProperty.FromReference(new uint[] { 10, 11 })),
            ("f19", EventProperty.FromReference(new[] { -1, -2 })),
            ("f20", (EventProperty)(string[])["alice", "bob"]),
            ("f21", EventProperty.FromReference(null)),
            ("f22", EventProperty.FromReference(new UnknownReference())));

        // Every StoredFieldKind is exercised: 13 packed + String/Sid/Guid/Bytes/UInt16Array/UInt32Array/Int32Array/
        // StringArray/Null/StringForm (the unknown reference degrades to its pooled string form).
        Assert.True(source.EventData.TryGetValue("f13", out EventFieldValue sourceString));
        Assert.Equal("hello", sourceString.AsString());

        ResolvedEvent detail = SealSingle(source);

        Assert.True(detail.EventData.TryGetValue("f13", out EventFieldValue detailString));
        Assert.Equal("hello", detailString.AsString());
        AssertFunctionalParity(source, detail);
    }

    [Fact]
    public void GetDetail_SealedEventDataPackedKinds_MatchStructurallyViaFromPacked()
    {
        ResolvedEvent source = EventWithProperties(
            ("i32", -70000),
            ("u64", 9_000_000_000UL),
            ("dbl", 2.5d),
            ("bln", true),
            ("dtm", new DateTime(2022, 3, 4, 5, 6, 7, DateTimeKind.Utc)));

        ResolvedEvent detail = SealSingle(source);

        // FromPacked reproduces (kind, bits) verbatim, so packed values round-trip to structural EventProperty equality.
        Assert.Equal(source.EventDataValues.Length, detail.EventDataValues.Length);

        for (int i = 0; i < source.EventDataValues.Length; i++)
        {
            Assert.Equal(source.EventDataValues[i], detail.EventDataValues[i]);
        }

        AssertFunctionalParity(source, detail);
    }

    [Fact]
    public void GetDetail_SealedFailClosedAndNoEventData_MatchObservableEventData()
    {
        // Row 0: a non-null schema whose value count matches NEITHER ordering (schema-present fail-closed, R4b).
        TemplateFieldSchema mismatchedSchema = new(allNames: ["a", "b", "c"], visibleNames: ["a", "b"]);
        ResolvedEvent nonNullSchemaNoMatch = EventWithSchema(mismatchedSchema, "v0", "v1", "v2", "v3");

        // Row 1: values present with a null schema (schema=null fail-closed).
        ResolvedEvent nullSchemaNoMatch = EventWithSchema(schema: null, "p0", "p1");

        // Row 2: no EventData at all -> Kind None.
        ResolvedEvent noEventData = new("live", LogPathType.Channel) { Id = 9 };

        Assert.Equal(EventDataKind.EventData, nonNullSchemaNoMatch.EventData.Kind);
        Assert.False(nonNullSchemaNoMatch.EventData.TryGetValue("a", out _));

        EventColumnStore store = EventColumnStore.Build(
            [nonNullSchemaNoMatch, nullSchemaNoMatch, noEventData], generation: 1, contentVersion: 1);

        ResolvedEvent nonNullDetail = store.GetDetail(0);
        Assert.Equal(EventDataKind.EventData, nonNullDetail.EventData.Kind);
        Assert.Equal(4, nonNullDetail.EventData.Count);
        Assert.False(nonNullDetail.EventData.TryGetValue("a", out _));
        AssertFunctionalParity(nonNullSchemaNoMatch, nonNullDetail);

        ResolvedEvent nullDetail = store.GetDetail(1);
        Assert.Equal(EventDataKind.EventData, nullDetail.EventData.Kind);
        Assert.Equal(2, nullDetail.EventData.Count);
        Assert.False(nullDetail.EventData.TryGetValue("p0", out _));
        AssertFunctionalParity(nullSchemaNoMatch, nullDetail);

        ResolvedEvent noneDetail = store.GetDetail(2);
        Assert.Equal(EventDataKind.None, noneDetail.EventData.Kind);
        Assert.Equal(0, noneDetail.EventData.Count);
        AssertFunctionalParity(noEventData, noneDetail);
    }

    [Fact]
    public void GetDetail_SealedFullyPopulatedScalars_MatchesSourceObservable()
    {
        ResolvedEvent source = new("live", LogPathType.Channel)
        {
            Id = 4624,
            TimeCreated = new DateTime(2021, 6, 15, 10, 20, 30, DateTimeKind.Utc),
            ComputerName = "PC1",
            Description = "A logon occurred.",
            Level = "Information",
            LogName = "Security",
            Source = "Microsoft-Windows-Security-Auditing",
            TaskCategory = "Logon",
            Xml = "<Event><System /></Event>",
            UserId = new SecurityIdentifier("S-1-5-18"),
            RecordId = 99L,
            ActivityId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ProcessId = 1234,
            ThreadId = 5678,
            Keywords = ["Audit Success", "Classic"]
        };

        ResolvedEvent detail = SealSingle(source);

        AssertFunctionalParity(source, detail);
    }

    [Fact]
    public void GetDetail_SealedKeywordsZeroOneMany_MatchSourceSequenceAndDisplayName()
    {
        ResolvedEvent none = new("live", LogPathType.Channel) { Id = 1 };
        ResolvedEvent one = new("live", LogPathType.Channel) { Id = 2, Keywords = ["Classic"] };
        ResolvedEvent many = new("live", LogPathType.Channel) { Id = 3, Keywords = ["Audit Success", "Audit Failure", "Classic"] };

        EventColumnStore store = EventColumnStore.Build([none, one, many], generation: 1, contentVersion: 1);

        AssertFunctionalParity(none, store.GetDetail(0));
        AssertFunctionalParity(one, store.GetDetail(1));
        AssertFunctionalParity(many, store.GetDetail(2));

        Assert.Equal(string.Empty, store.GetDetail(0).KeywordsDisplayName);
        Assert.Equal("Classic", store.GetDetail(1).KeywordsDisplayName);
        Assert.Equal("Audit Success, Audit Failure, Classic", store.GetDetail(2).KeywordsDisplayName);
    }

    [Fact]
    public void GetDetail_SealedSchemaVisibleAllEqualLengthDistinct_RoundTripsVisibleOrdering()
    {
        // VisibleNames.Length == AllNames.Length but the name lists differ; the value count matches both, so the store
        // resolves the Visible ordering first (R4a). Reconstruction must preserve that Visible-first precedence.
        TemplateFieldSchema schema = new(
            allNames: ["allA", "allB"],
            visibleNames: ["visA", "visB"]);
        ResolvedEvent source = EventWithSchema(schema, "x-value", "y-value");

        Assert.True(source.EventData.TryGetValue("visA", out _));
        Assert.False(source.EventData.TryGetValue("allA", out _));

        ResolvedEvent detail = SealSingle(source);

        Assert.True(detail.EventData.TryGetValue("visA", out EventFieldValue visible));
        Assert.Equal("x-value", visible.AsString());
        Assert.False(detail.EventData.TryGetValue("allA", out _));
        AssertFunctionalParity(source, detail);
    }

    [Fact]
    public void GetDetail_SealedUserDataIncomplete_MatchesTruncatedFallbackForAbsentPath()
    {
        ResolvedEvent source = new("live", LogPathType.Channel)
        {
            Id = 43,
            UserData = ImmutableArray.Create(
                new UserDataField("Kept", ImmutableArray.Create("value"), IsTruncated: false)),
            UserDataIncomplete = true
        };

        ResolvedEvent detail = SealSingle(source);

        Assert.True(detail.UserDataIncomplete);
        AssertFunctionalParity(source, detail);

        // With UserDataIncomplete set, an absent path yields a present-but-empty truncated result on both.
        StructuredFieldResult sourceAbsent = source.TryGetUserDataValues("absent");
        StructuredFieldResult detailAbsent = detail.TryGetUserDataValues("absent");
        Assert.True(sourceAbsent.IsTruncated);
        Assert.True(detailAbsent.IsTruncated);
        AssertUserDataLookupParity(source, detail, "absent");
    }

    [Fact]
    public void GetDetail_SealedUserDataMultiFieldValueTruncated_MatchesLookupParity()
    {
        ResolvedEvent source = new("live", LogPathType.Channel)
        {
            Id = 42,
            UserData = ImmutableArray.Create(
                new UserDataField("Result/@value", ImmutableArray.Create("ok", "retry"), IsTruncated: true),
                new UserDataField("Target", ImmutableArray.Create("svc"), IsTruncated: false))
        };

        ResolvedEvent detail = SealSingle(source);

        AssertFunctionalParity(source, detail);
        AssertUserDataLookupParity(source, detail, "Result/@value");
        AssertUserDataLookupParity(source, detail, "Target");
        AssertUserDataLookupParity(source, detail, "not/a/stored/path");
    }

    [Fact]
    public void GetDetailLean_PendingRow_ReturnsSamePendingEvent()
    {
        ResolvedEvent pending = BuildRichEvent();
        EventColumnStore store = EventColumnStore.Build([], generation: 1, contentVersion: 1).Append([pending]);

        Assert.True(store.IsPending(0));

        // R1: a pending lean read is O(1) and leaves the fully materialized detail fields intact.
        ResolvedEvent lean = store.GetDetailLean(0);
        Assert.Same(pending, lean);
    }

    [Fact]
    public void GetDetailLean_SealedRow_GridMatchesDetailWithEmptyDetailFields()
    {
        ResolvedEvent source = BuildRichEvent();
        EventColumnStore store = EventColumnStore.Build([source], generation: 1, contentVersion: 1);
        Assert.False(store.IsPending(0));

        ResolvedEvent full = store.GetDetail(0);
        ResolvedEvent lean = store.GetDetailLean(0);

        AssertGridParity(full, lean);

        // Detail-only fields are best-effort empty on the sealed lean projection.
        Assert.True(lean.UserData.IsDefaultOrEmpty);
        Assert.Equal(string.Empty, lean.Xml);
        Assert.Equal(EventDataKind.None, lean.EventData.Kind);
    }

    private static void AssertEnumerationParity(EventDataView expected, EventDataView actual)
    {
        List<EventDataView.Field> expectedFields = [];
        foreach (EventDataView.Field field in expected) { expectedFields.Add(field); }

        List<EventDataView.Field> actualFields = [];
        foreach (EventDataView.Field field in actual) { actualFields.Add(field); }

        Assert.Equal(expectedFields.Count, actualFields.Count);

        for (int i = 0; i < expectedFields.Count; i++)
        {
            Assert.Equal(expectedFields[i].Name, actualFields[i].Name);
            AssertFieldValueParity(expectedFields[i].Value, actualFields[i].Value);
        }
    }

    private static void AssertEventDataParity(ResolvedEvent expected, ResolvedEvent actual)
    {
        EventDataView expectedView = expected.EventData;
        EventDataView actualView = actual.EventData;

        Assert.Equal(expectedView.Kind, actualView.Kind);
        Assert.Equal(expectedView.Count, actualView.Count);

        for (int i = 0; i < expectedView.Count; i++)
        {
            bool expectedNamed = expectedView.TryGetName(i, out string expectedName);
            bool actualNamed = actualView.TryGetName(i, out string actualName);

            Assert.Equal(expectedNamed, actualNamed);
            Assert.Equal(expectedName, actualName);

            if (!expectedNamed) { continue; }

            Assert.True(expectedView.TryGetValue(expectedName, out EventFieldValue expectedByName));
            Assert.True(actualView.TryGetValue(actualName, out EventFieldValue actualByName));
            AssertFieldValueParity(expectedByName, actualByName);
        }

        AssertEnumerationParity(expectedView, actualView);
        AssertPropertyListParity(expected.EventDataValues, actual.EventDataValues);
    }

    private static void AssertFieldValueParity(EventFieldValue expected, EventFieldValue actual)
    {
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.AsString(), actual.AsString());

        if (expected.Kind == EventFieldValueKind.StringArray)
        {
            Assert.True(expected.TryGetStringArray(out string[]? expectedValues));
            Assert.True(actual.TryGetStringArray(out string[]? actualValues));
            Assert.Equal(expectedValues, actualValues);
        }
    }

    private static void AssertFunctionalParity(ResolvedEvent expected, ResolvedEvent actual)
    {
        Assert.Equal(expected.OwningLog, actual.OwningLog);
        Assert.Equal(expected.LogPathType, actual.LogPathType);
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.TimeCreated.Ticks, actual.TimeCreated.Ticks);
        Assert.Equal(expected.ComputerName, actual.ComputerName);
        Assert.Equal(expected.Description, actual.Description);
        Assert.Equal(expected.Level, actual.Level);
        Assert.Equal(expected.LogName, actual.LogName);
        Assert.Equal(expected.Source, actual.Source);
        Assert.Equal(expected.TaskCategory, actual.TaskCategory);
        Assert.Equal(expected.Xml, actual.Xml);
        Assert.Equal(expected.RecordId, actual.RecordId);
        Assert.Equal(expected.ActivityId, actual.ActivityId);
        Assert.Equal(expected.ProcessId, actual.ProcessId);
        Assert.Equal(expected.ThreadId, actual.ThreadId);
        Assert.Equal(expected.UserId, actual.UserId);
        Assert.Equal(expected.UserDataIncomplete, actual.UserDataIncomplete);

        Assert.Equal(expected.Keywords.ToArray(), actual.Keywords.ToArray());
        Assert.Equal(expected.KeywordsDisplayName, actual.KeywordsDisplayName);

        AssertUserDataParity(expected, actual);
        AssertEventDataParity(expected, actual);
    }

    private static void AssertGridParity(ResolvedEvent full, ResolvedEvent lean)
    {
        Assert.Equal(full.OwningLog, lean.OwningLog);
        Assert.Equal(full.LogPathType, lean.LogPathType);
        Assert.Equal(full.Id, lean.Id);
        Assert.Equal(full.TimeCreated.Ticks, lean.TimeCreated.Ticks);
        Assert.Equal(full.ComputerName, lean.ComputerName);
        Assert.Equal(full.Description, lean.Description);
        Assert.Equal(full.Level, lean.Level);
        Assert.Equal(full.LogName, lean.LogName);
        Assert.Equal(full.Source, lean.Source);
        Assert.Equal(full.TaskCategory, lean.TaskCategory);
        Assert.Equal(full.RecordId, lean.RecordId);
        Assert.Equal(full.ActivityId, lean.ActivityId);
        Assert.Equal(full.ProcessId, lean.ProcessId);
        Assert.Equal(full.ThreadId, lean.ThreadId);
        Assert.Equal(full.UserId, lean.UserId);
        Assert.Equal(full.UserDataIncomplete, lean.UserDataIncomplete);
        Assert.Equal(full.Keywords.ToArray(), lean.Keywords.ToArray());
        Assert.Equal(full.KeywordsDisplayName, lean.KeywordsDisplayName);
    }

    private static void AssertPropertyListParity(ImmutableArray<EventProperty> expected, ImmutableArray<EventProperty> actual)
    {
        if (expected.IsDefaultOrEmpty)
        {
            Assert.True(actual.IsDefaultOrEmpty);

            return;
        }

        Assert.Equal(expected.Length, actual.Length);

        for (int i = 0; i < expected.Length; i++)
        {
            AssertPropertyParity(expected[i], actual[i]);
        }
    }

    private static void AssertPropertyParity(EventProperty expected, EventProperty actual)
    {
        Assert.Equal(expected.Kind, actual.Kind);

        if (expected.Kind != EventPropertyKind.Reference)
        {
            // Packed kinds round-trip exactly through FromPacked, so structural EventProperty equality holds.
            Assert.Equal(expected, actual);

            return;
        }

        object? expectedReference = expected.Reference;
        object? actualReference = actual.Reference;

        if (expectedReference is null)
        {
            Assert.Null(actualReference);

            return;
        }

        // Match by EXACT runtime type: int[]/uint[] (and byte[]/sbyte[], ushort[]/short[]) are assignment-compatible,
        // so a type pattern would cross-capture. This mirrors the store's own exact-type dispatch.
        Type referenceType = expectedReference.GetType();

        if (referenceType == typeof(string))
        {
            Assert.Equal((string)expectedReference, actualReference as string);
        }
        else if (expectedReference is SecurityIdentifier expectedSid)
        {
            Assert.Equal(expectedSid, actualReference as SecurityIdentifier);
        }
        else if (expectedReference is Guid expectedGuid)
        {
            Assert.Equal(expectedGuid, (Guid)actualReference!);
        }
        else if (referenceType == typeof(byte[]))
        {
            Assert.Equal((byte[])expectedReference, (byte[])actualReference!);
        }
        else if (referenceType == typeof(ushort[]))
        {
            Assert.Equal((ushort[])expectedReference, (ushort[])actualReference!);
        }
        else if (referenceType == typeof(uint[]))
        {
            Assert.Equal((uint[])expectedReference, (uint[])actualReference!);
        }
        else if (referenceType == typeof(int[]))
        {
            Assert.Equal((int[])expectedReference, (int[])actualReference!);
        }
        else if (referenceType == typeof(string[]))
        {
            Assert.Equal((string[])expectedReference, (string[])actualReference!);
        }
        else
        {
            // StringForm fallback: an unexpected reference shape is stored/rehydrated as its pooled string form.
            Assert.Equal(EventFieldValue.FromProperty(expected).AsString(), actualReference as string);
        }
    }

    private static void AssertUserDataLookupParity(ResolvedEvent expected, ResolvedEvent actual, string path)
    {
        StructuredFieldResult expectedResult = expected.TryGetUserDataValues(path);
        StructuredFieldResult actualResult = actual.TryGetUserDataValues(path);

        Assert.Equal(expectedResult.IsTruncated, actualResult.IsTruncated);
        Assert.Equal(expectedResult.IsAbsent, actualResult.IsAbsent);
        Assert.Equal(expectedResult.Value.Kind, actualResult.Value.Kind);
        Assert.Equal(expectedResult.Value.AsString(), actualResult.Value.AsString());
        Assert.Equal(expectedResult.PresentValues.ToArray(), actualResult.PresentValues.ToArray());
    }

    private static void AssertUserDataParity(ResolvedEvent expected, ResolvedEvent actual)
    {
        if (!expected.UserData.IsDefaultOrEmpty)
        {
            foreach (UserDataField field in expected.UserData)
            {
                AssertUserDataLookupParity(expected, actual, field.Path);
            }
        }

        AssertUserDataLookupParity(expected, actual, "absent/path/for/parity");
    }

    private static ResolvedEvent BuildRichEvent() =>
        new ResolvedEvent("live", LogPathType.Channel)
        {
            Id = 4624,
            TimeCreated = new DateTime(2021, 6, 15, 10, 20, 30, DateTimeKind.Utc),
            ComputerName = "PC1",
            Description = "A logon occurred.",
            Level = "Information",
            LogName = "Security",
            Source = "Microsoft-Windows-Security-Auditing",
            TaskCategory = "Logon",
            Xml = "<Event><System /></Event>",
            UserId = new SecurityIdentifier("S-1-5-18"),
            RecordId = 99L,
            ActivityId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            ProcessId = 1234,
            ThreadId = 5678,
            Keywords = ["Audit Success", "Classic"],
            UserData = ImmutableArray.Create(
                new UserDataField("Result/@value", ImmutableArray.Create("ok", "retry"), IsTruncated: true),
                new UserDataField("Target", ImmutableArray.Create("svc"), IsTruncated: false)),
            UserDataIncomplete = true
        }.WithEventData(("Operation", "Read"), ("Count", 3), ("Succeeded", true));

    private static ResolvedEvent EventWithProperties(params (string Name, EventProperty Value)[] fields)
    {
        string template = "<template>"
            + string.Concat(fields.Select(field => $"<data name=\"{SecurityElement.Escape(field.Name)}\"/>"))
            + "</template>";

        TemplateFieldSchema schema = new TemplateAnalyzer().GetTemplateInfo(template).Schema;

        ImmutableArray<EventProperty>.Builder values = ImmutableArray.CreateBuilder<EventProperty>(fields.Length);

        foreach ((string _, EventProperty value) in fields) { values.Add(value); }

        return new ResolvedEvent("live", LogPathType.Channel)
        {
            EventDataValues = values.MoveToImmutable(),
            EventDataSchema = schema
        };
    }

    private static ResolvedEvent EventWithSchema(TemplateFieldSchema? schema, params EventProperty[] values) =>
        new("live", LogPathType.Channel)
        {
            EventDataValues = [.. values],
            EventDataSchema = schema
        };

    private static ResolvedEvent SealSingle(ResolvedEvent source)
    {
        EventColumnStore store = EventColumnStore.Build([source], generation: 1, contentVersion: 1);
        Assert.False(store.IsPending(0));

        return store.GetDetail(0);
    }

    private sealed class UnknownReference
    {
        public override string ToString() => "unknown-ref";
    }
}
