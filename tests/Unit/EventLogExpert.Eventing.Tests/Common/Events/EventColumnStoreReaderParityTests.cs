// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Eventing.Structured;
using EventLogExpert.Eventing.TestUtils;
using System.Collections.Immutable;
using System.Security;
using System.Security.Principal;

namespace EventLogExpert.Eventing.Tests.Common.Events;

public sealed class EventColumnStoreReaderParityTests
{
    private const long ContentVersion = 42;
    private const int Generation = 3;

    private static readonly EventLogId s_logId = EventLogId.Create();

    // Every corpus event stamps TimeCreated as Utc: the column store always reconstructs TimeCreated with
    // DateTimeKind.Utc, and EventFieldValue.AsString() renders a Utc DateTime with a trailing "Z" (round ("O") format),
    // so an Unspecified-kind source would diverge only in the suffix and break differential parity.
    private static readonly DateTime s_time = new(2021, 6, 15, 10, 20, 30, DateTimeKind.Utc);

    [Fact]
    public void EnumerateEventData_PendingRows_MatchLegacyReader()
    {
        (IEventColumnReader legacy, IEventColumnReader column, ResolvedEvent[] corpus) = BuildPending();

        AssertEventDataEnumerationParity(legacy, column, corpus.Length);
    }

    [Fact]
    public void EnumerateEventData_SealedRows_MatchLegacyReader()
    {
        (IEventColumnReader legacy, IEventColumnReader column, ResolvedEvent[] corpus) = BuildSealed();

        AssertEventDataEnumerationParity(legacy, column, corpus.Length);
    }

    [Fact]
    public void EnumerateUserData_PendingRows_MatchLegacyReader()
    {
        (IEventColumnReader legacy, IEventColumnReader column, ResolvedEvent[] corpus) = BuildPending();

        AssertUserDataEnumerationParity(legacy, column, corpus.Length);
    }

    [Fact]
    public void EnumerateUserData_SealedRows_MatchLegacyReader()
    {
        (IEventColumnReader legacy, IEventColumnReader column, ResolvedEvent[] corpus) = BuildSealed();

        AssertUserDataEnumerationParity(legacy, column, corpus.Length);
    }

    [Fact]
    public void GetField_ForeignLogIdLocator_ThrowsArgumentException()
    {
        (IEventColumnReader legacy, IEventColumnReader column, _) = BuildSealed();
        EventLocator foreign = new(EventLogId.Create(), column.Generation, 0);

        Assert.Throws<ArgumentException>(() => column.GetField(foreign, EventFieldId.Id));
        Assert.Throws<ArgumentException>(() => legacy.GetField(foreign, EventFieldId.Id));
    }

    [Fact]
    public void GetField_PendingRowsForEveryFieldId_MatchLegacyReader()
    {
        (IEventColumnReader legacy, IEventColumnReader column, ResolvedEvent[] corpus) = BuildPending();

        AssertGetFieldParity(legacy, column, corpus.Length);
    }

    [Fact]
    public void GetField_SealedRowsForEveryFieldId_MatchLegacyReader()
    {
        (IEventColumnReader legacy, IEventColumnReader column, ResolvedEvent[] corpus) = BuildSealed();

        AssertGetFieldParity(legacy, column, corpus.Length);
    }

    [Fact]
    public void GetField_StaleGenerationLocator_ThrowsArgumentException()
    {
        (IEventColumnReader legacy, IEventColumnReader column, _) = BuildSealed();
        EventLocator stale = new(column.LogId, column.Generation + 1, 0);

        Assert.Throws<ArgumentException>(() => column.GetField(stale, EventFieldId.Id));
        Assert.Throws<ArgumentException>(() => legacy.GetField(stale, EventFieldId.Id));
    }

    [Fact]
    public void GetKeywords_PendingRows_MatchLegacyReader()
    {
        (IEventColumnReader legacy, IEventColumnReader column, ResolvedEvent[] corpus) = BuildPending();

        AssertKeywordsParity(legacy, column, corpus.Length);
    }

    [Fact]
    public void GetKeywords_SealedRows_MatchLegacyReader()
    {
        (IEventColumnReader legacy, IEventColumnReader column, ResolvedEvent[] corpus) = BuildSealed();

        AssertKeywordsParity(legacy, column, corpus.Length);
    }

    [Fact]
    public void GetUserData_PendingRowsForEveryProbeKey_MatchLegacyReader()
    {
        (IEventColumnReader legacy, IEventColumnReader column, ResolvedEvent[] corpus) = BuildPending();

        AssertUserDataParity(legacy, column, corpus);
    }

    [Fact]
    public void GetUserData_SealedRowsForEveryProbeKey_MatchLegacyReader()
    {
        (IEventColumnReader legacy, IEventColumnReader column, ResolvedEvent[] corpus) = BuildSealed();

        AssertUserDataParity(legacy, column, corpus);
    }

    [Fact]
    public void GetUserDataIncomplete_PendingRows_MatchLegacyReader()
    {
        (IEventColumnReader legacy, IEventColumnReader column, ResolvedEvent[] corpus) = BuildPending();

        AssertUserDataIncompleteParity(legacy, column, corpus.Length);
    }

    [Fact]
    public void GetUserDataIncomplete_SealedRows_MatchLegacyReader()
    {
        (IEventColumnReader legacy, IEventColumnReader column, ResolvedEvent[] corpus) = BuildSealed();

        AssertUserDataIncompleteParity(legacy, column, corpus.Length);
    }

    [Fact]
    public void HResultScans_SealedAndPending_MatchLegacyReader()
    {
        string[] providers = ["Microsoft-Windows-WindowsUpdateClient", "Microsoft-Windows-Servicing"];
        long[] targetCodes = [0x800F0823L, 0x800F081FL];

        foreach (bool sealRows in new[] { true, false })
        {
            (IEventColumnReader legacy, IEventColumnReader column) = BuildErrorCodeReaders(sealRows);
            int[] rank = AllSurvive(legacy.Count);

            var legacyCounts = new Dictionary<long, int>();
            var columnCounts = new Dictionary<long, int>();
            legacy.CountEventDataHResults(rank, "errorCode", providers, legacyCounts, CancellationToken.None);
            column.CountEventDataHResults(rank, "errorCode", providers, columnCounts, CancellationToken.None);
            Assert.Equal(legacyCounts.OrderBy(pair => pair.Key), columnCounts.OrderBy(pair => pair.Key));

            int[] legacySlots = new int[targetCodes.Length + 1];
            int[] columnSlots = new int[targetCodes.Length + 1];
            legacy.BucketTimeTicksByEventDataHResult(rank, 0, long.MaxValue, 1, "errorCode", providers, targetCodes, legacySlots, CancellationToken.None);
            column.BucketTimeTicksByEventDataHResult(rank, 0, long.MaxValue, 1, "errorCode", providers, targetCodes, columnSlots, CancellationToken.None);
            Assert.Equal(legacySlots, columnSlots);
        }
    }

    [Fact]
    public void Surface_PendingStore_MatchesLegacyReader()
    {
        (IEventColumnReader legacy, IEventColumnReader column, _) = BuildPending();

        AssertSurfaceParity(legacy, column);
    }

    [Fact]
    public void Surface_SealedStore_MatchesLegacyReader()
    {
        (IEventColumnReader legacy, IEventColumnReader column, _) = BuildSealed();

        AssertSurfaceParity(legacy, column);
    }

    [Fact]
    public void TryGetEventData_PendingRowsForEveryProbeName_MatchLegacyReader()
    {
        (IEventColumnReader legacy, IEventColumnReader column, ResolvedEvent[] corpus) = BuildPending();

        AssertEventDataParity(legacy, column, corpus);
    }

    [Fact]
    public void TryGetEventData_SealedRowsForEveryProbeName_MatchLegacyReader()
    {
        (IEventColumnReader legacy, IEventColumnReader column, ResolvedEvent[] corpus) = BuildSealed();

        AssertEventDataParity(legacy, column, corpus);
    }

    private static int[] AllSurvive(int count)
    {
        int[] rank = new int[count];

        for (int index = 0; index < count; index++) { rank[index] = index; }

        return rank;
    }

    private static void AssertEventDataEnumerationParity(IEventColumnReader legacy, IEventColumnReader column, int count)
    {
        for (int index = 0; index < count; index++)
        {
            List<(string Name, EventFieldValueKind Kind, string Value)> expected = MaterializeEventData(legacy, legacy.LocatorAt(index));
            List<(string Name, EventFieldValueKind Kind, string Value)> actual = MaterializeEventData(column, column.LocatorAt(index));

            Assert.Equal(expected, actual);
        }
    }

    private static void AssertEventDataParity(IEventColumnReader legacy, IEventColumnReader column, ResolvedEvent[] corpus)
    {
        List<string> probeNames = CollectEventDataNames(corpus);

        for (int index = 0; index < corpus.Length; index++)
        {
            foreach (string name in probeNames)
            {
                bool expectedFound = legacy.TryGetEventData(legacy.LocatorAt(index), name, out EventFieldValue expected);
                bool actualFound = column.TryGetEventData(column.LocatorAt(index), name, out EventFieldValue actual);

                Assert.Equal(expectedFound, actualFound);
                Assert.Equal(expected.Kind, actual.Kind);
                Assert.Equal(expected.AsString(), actual.AsString());
            }
        }
    }

    private static void AssertGetFieldParity(IEventColumnReader legacy, IEventColumnReader column, int count)
    {
        foreach (EventFieldId field in Enum.GetValues<EventFieldId>())
        {
            for (int index = 0; index < count; index++)
            {
                EventFieldValue expected = legacy.GetField(legacy.LocatorAt(index), field);
                EventFieldValue actual = column.GetField(column.LocatorAt(index), field);

                Assert.Equal(expected.Kind, actual.Kind);
                Assert.Equal(expected.AsString(), actual.AsString());
            }
        }
    }

    private static void AssertKeywordsParity(IEventColumnReader legacy, IEventColumnReader column, int count)
    {
        for (int index = 0; index < count; index++)
        {
            IReadOnlyList<string> expected = legacy.GetKeywords(legacy.LocatorAt(index));
            IReadOnlyList<string> actual = column.GetKeywords(column.LocatorAt(index));

            Assert.Equal(expected, actual);
        }
    }

    private static void AssertSurfaceParity(IEventColumnReader legacy, IEventColumnReader column)
    {
        Assert.Equal(legacy.LogId, column.LogId);
        Assert.Equal(legacy.Generation, column.Generation);
        Assert.Equal(legacy.ContentVersion, column.ContentVersion);
        Assert.Equal(legacy.Count, column.Count);
    }

    private static void AssertUserDataEnumerationParity(IEventColumnReader legacy, IEventColumnReader column, int count)
    {
        for (int index = 0; index < count; index++)
        {
            List<(string Path, EventFieldValueKind Kind, string Value, bool Truncated, bool Absent)> expected =
                MaterializeUserData(legacy, legacy.LocatorAt(index));
            List<(string Path, EventFieldValueKind Kind, string Value, bool Truncated, bool Absent)> actual =
                MaterializeUserData(column, column.LocatorAt(index));

            Assert.Equal(expected, actual);
        }
    }

    private static void AssertUserDataIncompleteParity(IEventColumnReader legacy, IEventColumnReader column, int count)
    {
        for (int index = 0; index < count; index++)
        {
            Assert.Equal(
                legacy.GetUserDataIncomplete(legacy.LocatorAt(index)),
                column.GetUserDataIncomplete(column.LocatorAt(index)));
        }
    }

    private static void AssertUserDataParity(IEventColumnReader legacy, IEventColumnReader column, ResolvedEvent[] corpus)
    {
        List<string> probeKeys = CollectUserDataKeys(corpus);

        for (int index = 0; index < corpus.Length; index++)
        {
            foreach (string key in probeKeys)
            {
                StructuredFieldResult expected = legacy.GetUserData(legacy.LocatorAt(index), key);
                StructuredFieldResult actual = column.GetUserData(column.LocatorAt(index), key);

                Assert.Equal(expected.IsAbsent, actual.IsAbsent);
                Assert.Equal(expected.IsTruncated, actual.IsTruncated);
                Assert.Equal(expected.PresentValues.ToArray(), actual.PresentValues.ToArray());
            }
        }
    }

    private static ResolvedEvent[] BuildCorpus() =>
    [
        // Fully populated scalars, non-null UserId, many keywords, and every side column present (keywords + UserData +
        // named EventData) so the chunk's per-row offset arithmetic is exercised on a dense row.
        new ResolvedEvent("Security", LogPathType.Channel)
        {
            Id = 4624,
            TimeCreated = s_time,
            Level = "Information",
            ComputerName = "PC1",
            Source = "Microsoft-Windows-Security-Auditing",
            TaskCategory = "Logon",
            Opcode = "Start",
            LogName = "Security",
            Description = "An account was logged on.",
            Xml = "<Event><System /></Event>",
            UserId = new SecurityIdentifier("S-1-5-18"),
            RecordId = 99L,
            ActivityId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            RelatedActivityId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            ProcessId = 1234,
            ThreadId = 5678,
            Keywords = ["Audit Success", "Classic"],
            UserData = ImmutableArray.Create(new UserDataField("Logon/@type", ImmutableArray.Create("2"), IsTruncated: false))
        }.WithEventData(("Operation", "Read"), ("Count", 3), ("Succeeded", true)),

        // Absent nullables, null UserId, zero keywords, empty pooled fields, no EventData.
        new ResolvedEvent("Application", LogPathType.Channel)
        {
            Id = 1000,
            TimeCreated = s_time,
            Level = "Error"
        },

        // Exactly one keyword.
        new ResolvedEvent("System", LogPathType.Channel)
        {
            Id = 7,
            TimeCreated = s_time,
            Keywords = ["Classic"]
        },

        // Multi-field UserData with one truncated field (event-level UserDataIncomplete stays false).
        new ResolvedEvent("live", LogPathType.Channel)
        {
            Id = 11,
            TimeCreated = s_time,
            UserData = ImmutableArray.Create(
                new UserDataField("Result/@value", ImmutableArray.Create("ok", "retry"), IsTruncated: true),
                new UserDataField("Target", ImmutableArray.Create("svc"), IsTruncated: false))
        },

        // UserDataIncomplete = true with a present field: a matched field's truncation ORs in the event-level flag, and
        // an absent key yields the present-but-empty truncated result rather than a decisive no-match.
        new ResolvedEvent("live", LogPathType.Channel)
        {
            Id = 12,
            TimeCreated = s_time,
            UserData = ImmutableArray.Create(new UserDataField("Path", ImmutableArray.Create("v1"), IsTruncated: false)),
            UserDataIncomplete = true
        },

        // EventData covering every StoredFieldKind, including StringForm via a non-array unknown reference that degrades
        // to its pooled string form (not an array).
        WithNamedProperties(
            new ResolvedEvent("live", LogPathType.Channel) { Id = 20, TimeCreated = s_time },
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
            ("f22", EventProperty.FromReference(new UnknownReference()))),

        // Fail-closed: a non-null schema whose value count matches neither the visible (2) nor the all (3) ordering, so
        // named access is disabled (schema id -1) even though the values are stored positionally.
        WithRawSchema(
            new ResolvedEvent("live", LogPathType.Channel) { Id = 30, TimeCreated = s_time },
            new TemplateFieldSchema(["a", "b", "c"], ["a", "b"]),
            "v1", "v2", "v3", "v4"),

        // No schema but with values: named access is disabled, values remain positional.
        WithRawSchema(
            new ResolvedEvent("live", LogPathType.Channel) { Id = 31, TimeCreated = s_time },
            schema: null,
            "v1", "v2"),

        // Duplicate EventData name: the first index wins for the shared name.
        new ResolvedEvent("live", LogPathType.Channel) { Id = 40, TimeCreated = s_time }
            .WithEventData(("Dup", "first"), ("Dup", "second"), ("Other", "x")),

        // Length-provider schema (AllNames longer than VisibleNames) with a value count matching the all ordering: the
        // provider name resolves under the all ordering.
        WithRawSchema(
            new ResolvedEvent("live", LogPathType.Channel) { Id = 50, TimeCreated = s_time },
            new TemplateFieldSchema(["visA", "visB", "LengthProvider"], ["visA", "visB"]),
            "a", "b", "len"),

        // Same length-provider schema with a value count matching the visible ordering: the provider name is not
        // resolvable, exactly as the visible ordering excludes it.
        WithRawSchema(
            new ResolvedEvent("live", LogPathType.Channel) { Id = 51, TimeCreated = s_time },
            new TemplateFieldSchema(["visA", "visB", "LengthProvider"], ["visA", "visB"]),
            "a", "b"),

        // Positional-empty EventData field NAME at a middle position (design-required "positional-empty node" edge): both
        // readers must yield the ("", value) pair at that position, keeping the enumerated name sequence at parity.
        WithRawSchema(
            new ResolvedEvent("live", LogPathType.Channel) { Id = 60, TimeCreated = s_time },
            new TemplateFieldSchema(["a", "", "c"], ["a", "", "c"]),
            "v0", "v1", "v2")
    ];

    private static ResolvedEvent[] BuildErrorCodeCorpus() =>
    [
        ErrorCodeEvent("Microsoft-Windows-WindowsUpdateClient", unchecked((int)0x800F0823u), 0),
        ErrorCodeEvent("Microsoft-Windows-Servicing", "0x800F081F", 1),
        ErrorCodeEvent("Microsoft-Windows-WindowsUpdateClient", 0, 2),
        ErrorCodeEvent("Microsoft-Windows-WindowsUpdateClient", unchecked((int)0x80070005u), 3),
        new ResolvedEvent("TestLog", LogPathType.Channel) { Id = 44, Source = "Microsoft-Windows-WindowsUpdateClient", TimeCreated = s_time.AddMinutes(4) },
        ErrorCodeEvent("Some-Other-Provider", unchecked((int)0x800F0823u), 5)
    ];

    private static (IEventColumnReader Legacy, IEventColumnReader Column) BuildErrorCodeReaders(bool sealRows)
    {
        ResolvedEvent[] corpus = BuildErrorCodeCorpus();
        EventColumnStore store = sealRows
            ? EventColumnStore.Build(corpus, Generation, ContentVersion)
            : EventColumnStore.Build([], Generation, ContentVersion).Append(corpus);

        return (new LegacyEventColumnReader(s_logId, store.Generation, store.ContentVersion, corpus), new EventColumnStoreReader(s_logId, store));
    }

    private static (IEventColumnReader Legacy, IEventColumnReader Column, ResolvedEvent[] Corpus) BuildPending()
    {
        ResolvedEvent[] corpus = BuildCorpus();
        EventColumnStore store = EventColumnStore.Build([], Generation, ContentVersion).Append(corpus);

        Assert.Equal(0, store.SealedCount);

        LegacyEventColumnReader legacy = new(s_logId, store.Generation, store.ContentVersion, corpus);
        EventColumnStoreReader column = new(s_logId, store);

        return (legacy, column, corpus);
    }

    // Build the legacy reader from the store's generation/content version: Append bumps ContentVersion, so surface
    // parity holds only when the legacy adapter is stamped with the same values the store settled on.
    private static (IEventColumnReader Legacy, IEventColumnReader Column, ResolvedEvent[] Corpus) BuildSealed()
    {
        ResolvedEvent[] corpus = BuildCorpus();
        EventColumnStore store = EventColumnStore.Build(corpus, Generation, ContentVersion);

        Assert.Equal(corpus.Length, store.SealedCount);

        LegacyEventColumnReader legacy = new(s_logId, store.Generation, store.ContentVersion, corpus);
        EventColumnStoreReader column = new(s_logId, store);

        return (legacy, column, corpus);
    }

    private static List<string> CollectEventDataNames(ResolvedEvent[] corpus)
    {
        HashSet<string> names = new(StringComparer.Ordinal);

        foreach (ResolvedEvent resolvedEvent in corpus)
        {
            if (resolvedEvent.EventDataSchema is { } schema)
            {
                foreach (string name in schema.AllNames) { names.Add(name); }
                foreach (string name in schema.VisibleNames) { names.Add(name); }
            }
        }

        names.Add("NoSuchField");
        names.Add(string.Empty);

        return [.. names];
    }

    private static List<string> CollectUserDataKeys(ResolvedEvent[] corpus)
    {
        HashSet<string> keys = new(StringComparer.Ordinal);

        foreach (ResolvedEvent resolvedEvent in corpus)
        {
            if (!resolvedEvent.UserData.IsDefaultOrEmpty)
            {
                foreach (UserDataField field in resolvedEvent.UserData) { keys.Add(field.Path); }
            }
        }

        keys.Add("NoSuchKey");

        return [.. keys];
    }

    private static ResolvedEvent ErrorCodeEvent(string source, object errorCode, int index) =>
        new ResolvedEvent("TestLog", LogPathType.Channel) { Id = 20, Source = source, TimeCreated = s_time.AddMinutes(index) }
            .WithEventData(("errorCode", errorCode));

    private static List<(string Name, EventFieldValueKind Kind, string Value)> MaterializeEventData(
        IEventColumnReader reader,
        EventLocator locator)
    {
        List<(string Name, EventFieldValueKind Kind, string Value)> fields = [];

        foreach (EventDataView.Field field in reader.EnumerateEventData(locator))
        {
            fields.Add((field.Name, field.Value.Kind, field.Value.AsString()));
        }

        return fields;
    }

    private static List<(string Path, EventFieldValueKind Kind, string Value, bool Truncated, bool Absent)> MaterializeUserData(
        IEventColumnReader reader,
        EventLocator locator)
    {
        List<(string Path, EventFieldValueKind Kind, string Value, bool Truncated, bool Absent)> fields = [];

        foreach (UserDataFieldEntry entry in reader.EnumerateUserData(locator))
        {
            fields.Add((
                entry.Path,
                entry.Result.Value.Kind,
                entry.Result.Value.AsString(),
                entry.Result.IsTruncated,
                entry.Result.IsAbsent));
        }

        return fields;
    }

    private static ResolvedEvent WithNamedProperties(ResolvedEvent source, params (string Name, EventProperty Value)[] fields)
    {
        string template = "<template>"
            + string.Concat(fields.Select(field => $"<data name=\"{SecurityElement.Escape(field.Name)}\"/>"))
            + "</template>";

        TemplateFieldSchema schema = new TemplateAnalyzer().GetTemplateInfo(template).Schema;
        ImmutableArray<EventProperty>.Builder values = ImmutableArray.CreateBuilder<EventProperty>(fields.Length);

        foreach ((string _, EventProperty value) in fields) { values.Add(value); }

        return source with { EventDataValues = values.MoveToImmutable(), EventDataSchema = schema };
    }

    private static ResolvedEvent WithRawSchema(ResolvedEvent source, TemplateFieldSchema? schema, params EventProperty[] values) =>
        source with { EventDataValues = [.. values], EventDataSchema = schema };

    private sealed class UnknownReference
    {
        public override string ToString() => "unknown-ref";
    }
}
