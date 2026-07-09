// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using System.Security.Principal;

namespace EventLogExpert.Eventing.Tests.Common.Events;

/// <summary>
///     Validates the bulk column materializers on <see cref="IEventColumnReader" /> (
///     <see cref="IEventColumnReader.CopyInt64Column" />, <see cref="IEventColumnReader.CopyGuidColumn" />,
///     <see cref="IEventColumnReader.CopyPoolIndexColumn" />, and <see cref="IEventColumnReader.Pool" />) against the
///     per-row <see cref="IEventColumnReader.GetField" /> oracle for a sealed store, an all-pending store, and a mixed
///     store whose pending tail introduces pooled strings the sealed pool never interned. The legacy adapter answers the
///     same API, so both backends are checked.
/// </summary>
public sealed class EventColumnReaderBulkColumnTests
{
    private const long ContentVersion = 42;
    private const int Generation = 3;

    private static readonly EventFieldId[] s_int64Fields =
        [EventFieldId.Id, EventFieldId.RecordId, EventFieldId.ProcessId, EventFieldId.ThreadId, EventFieldId.TimeCreated];

    private static readonly EventLogId s_logId = EventLogId.Create();

    private static readonly EventFieldId[] s_pooledFields =
    [
        EventFieldId.Level, EventFieldId.LogName, EventFieldId.ComputerName, EventFieldId.Source,
        EventFieldId.TaskCategory, EventFieldId.UserId, EventFieldId.Description, EventFieldId.Xml, EventFieldId.OwningLog
    ];
    private static readonly DateTime s_time = new(2021, 6, 15, 10, 20, 30, DateTimeKind.Utc);

    [Fact]
    public void BulkColumns_LegacyReader_MatchGetFieldForEveryRow()
    {
        ResolvedEvent[] corpus = BuildCorpus();

        AssertBulkColumnParity(new LegacyEventColumnReader(s_logId, Generation, ContentVersion, corpus));
    }

    [Fact]
    public void BulkColumns_MixedStore_WithNovelPendingPoolStrings_MatchGetFieldForEveryRow()
    {
        ResolvedEvent[] sealedCorpus = BuildCorpus();
        ResolvedEvent[] pendingCorpus = BuildNovelPendingCorpus();
        EventColumnStore store = EventColumnStore.Build(sealedCorpus, Generation, ContentVersion).Append(pendingCorpus);

        Assert.Equal(sealedCorpus.Length, store.SealedCount);
        Assert.Equal(sealedCorpus.Length + pendingCorpus.Length, store.Count);

        var reader = new EventColumnStoreReader(s_logId, store);
        AssertBulkColumnParity(reader);

        // A pending Source the sealed pool never interned must resolve through the pool extension, i.e. to an index at or
        // beyond the sealed pool's distinct count.
        var poolIndices = new int[store.Count];
        reader.CopyPoolIndexColumn(EventFieldId.Source, poolIndices);
        int novelIndex = poolIndices[store.Count - 1];

        Assert.True(novelIndex >= store.PoolDistinctCount, "novel pending Source should map into the pool extension");
        Assert.Equal("NovelPendingSource", reader.Pool[novelIndex]);
    }

    [Fact]
    public void BulkColumns_PendingStore_MatchGetFieldForEveryRow()
    {
        ResolvedEvent[] corpus = BuildCorpus();
        EventColumnStore store = EventColumnStore.Build([], Generation, ContentVersion).Append(corpus);

        Assert.Equal(0, store.SealedCount);

        AssertBulkColumnParity(new EventColumnStoreReader(s_logId, store));
    }

    [Fact]
    public void BulkColumns_SealedStore_MatchGetFieldForEveryRow()
    {
        ResolvedEvent[] corpus = BuildCorpus();
        EventColumnStore store = EventColumnStore.Build(corpus, Generation, ContentVersion);

        Assert.Equal(corpus.Length, store.SealedCount);

        AssertBulkColumnParity(new EventColumnStoreReader(s_logId, store));
    }

    [Fact]
    public void BulkColumns_WrongStorageKind_Throws()
    {
        ResolvedEvent[] corpus = BuildCorpus();
        var reader = new EventColumnStoreReader(s_logId, EventColumnStore.Build(corpus, Generation, ContentVersion));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => reader.CopyInt64Column(EventFieldId.Level, new long[corpus.Length], new bool[corpus.Length]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => reader.CopyGuidColumn(EventFieldId.Id, new Guid[corpus.Length], new bool[corpus.Length]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => reader.CopyPoolIndexColumn(EventFieldId.RecordId, new int[corpus.Length]));
    }

    [Fact]
    public void CopyPoolIndexColumn_KeywordsDisplay_Throws()
    {
        ResolvedEvent[] corpus = BuildCorpus();
        var reader = new EventColumnStoreReader(s_logId, EventColumnStore.Build(corpus, Generation, ContentVersion));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => reader.CopyPoolIndexColumn(EventFieldId.KeywordsDisplay, new int[corpus.Length]));
    }

    private static void AssertBulkColumnParity(IEventColumnReader reader)
    {
        int count = reader.Count;

        foreach (EventFieldId field in s_int64Fields)
        {
            var values = new long[count];
            var hasValue = new bool[count];
            reader.CopyInt64Column(field, values, hasValue);

            for (int index = 0; index < count; index++)
            {
                EventFieldValue expected = reader.GetField(reader.LocatorAt(index), field);

                if (field == EventFieldId.TimeCreated)
                {
                    Assert.True(hasValue[index], $"{field}[{index}] should always be present");
                    Assert.True(expected.TryGetDateTime(out DateTime dateTime));
                    Assert.Equal(dateTime.Ticks, values[index]);

                    continue;
                }

                if (expected.Kind == EventFieldValueKind.Null)
                {
                    Assert.False(hasValue[index], $"{field}[{index}] should be absent");

                    continue;
                }

                Assert.True(hasValue[index], $"{field}[{index}] should be present");
                Assert.True(expected.TryGetInt64(out long value));
                Assert.Equal(value, values[index]);
            }
        }

        var activityValues = new Guid[count];
        var activityHasValue = new bool[count];
        reader.CopyGuidColumn(EventFieldId.ActivityId, activityValues, activityHasValue);

        for (int index = 0; index < count; index++)
        {
            EventFieldValue expected = reader.GetField(reader.LocatorAt(index), EventFieldId.ActivityId);

            if (expected.Kind == EventFieldValueKind.Null)
            {
                Assert.False(activityHasValue[index], $"ActivityId[{index}] should be absent");

                continue;
            }

            Assert.True(activityHasValue[index], $"ActivityId[{index}] should be present");
            Assert.True(expected.TryGetGuid(out Guid guid));
            Assert.Equal(guid, activityValues[index]);
        }

        IReadOnlyList<string?> pool = reader.Pool;

        foreach (EventFieldId field in s_pooledFields)
        {
            var poolIndices = new int[count];
            reader.CopyPoolIndexColumn(field, poolIndices);

            for (int index = 0; index < count; index++)
            {
                string expected = reader.GetField(reader.LocatorAt(index), field).AsString();
                int poolIndex = poolIndices[index];

                if (poolIndex < 0)
                {
                    Assert.Equal(string.Empty, expected);

                    continue;
                }

                Assert.Equal(expected, pool[poolIndex]);
            }
        }
    }

    private static ResolvedEvent[] BuildCorpus() =>
    [
        new ResolvedEvent("Security", LogPathType.Channel)
        {
            Id = 4624,
            TimeCreated = s_time,
            Level = "Information",
            ComputerName = "PC1",
            Source = "Microsoft-Windows-Security-Auditing",
            TaskCategory = "Logon",
            LogName = "Security",
            Description = "An account was logged on.",
            Xml = "<Event><System /></Event>",
            UserId = new SecurityIdentifier("S-1-5-18"),
            RecordId = 99L,
            ActivityId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            ProcessId = 1234,
            ThreadId = 5678,
            Keywords = ["Audit Success", "Classic"]
        },

        // Absent nullables, null UserId, empty pooled fields.
        new ResolvedEvent("Application", LogPathType.Channel)
        {
            Id = 1000,
            TimeCreated = s_time,
            Level = "Error"
        },

        // Shares pooled values with the first row (dedups in the pool) but distinct scalars.
        new ResolvedEvent("Security", LogPathType.Channel)
        {
            Id = 7,
            TimeCreated = s_time.AddMinutes(1),
            Level = "Information",
            ComputerName = "PC1",
            Source = "Microsoft-Windows-Security-Auditing",
            LogName = "Security",
            UserId = new SecurityIdentifier("S-1-5-18"),
            RecordId = 100L,
            ProcessId = 1234
        },

        new ResolvedEvent("System", LogPathType.Channel)
        {
            Id = 11,
            TimeCreated = s_time.AddMinutes(2),
            Level = "Warning",
            ComputerName = "PC2",
            Source = "Service Control Manager",
            LogName = "System",
            TaskCategory = "None",
            UserId = new SecurityIdentifier("S-1-5-21-1-2-3-1001"),
            RecordId = 101L,
            ActivityId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            ThreadId = 22
        }
    ];

    private static ResolvedEvent[] BuildNovelPendingCorpus() =>
    [
        // Pooled strings that never appear in BuildCorpus, so the sealed pool cannot index them.
        new ResolvedEvent("NovelPendingLog", LogPathType.Channel)
        {
            Id = 500,
            TimeCreated = s_time.AddHours(1),
            Level = "NovelPendingLevel",
            ComputerName = "NovelPendingHost",
            Source = "NovelPendingSource",
            TaskCategory = "NovelPendingTask",
            LogName = "NovelPendingName",
            Description = "NovelPendingDescription",
            Xml = "<NovelPendingXml />",
            UserId = new SecurityIdentifier("S-1-5-21-9-9-9-9999"),
            RecordId = 900L,
            ActivityId = Guid.Parse("55555555-5555-5555-5555-555555555555"),
            ProcessId = 909,
            ThreadId = 808
        }
    ];
}
