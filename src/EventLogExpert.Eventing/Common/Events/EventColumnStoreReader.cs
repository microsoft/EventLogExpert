// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Structured;
using System.Collections;
using System.Security.Principal;

namespace EventLogExpert.Eventing.Common.Events;

internal sealed class EventColumnStoreReader : IEventColumnReader
{
    private readonly EventColumnStore _store;

    // Lazily built the first time a pooled column is materialized: interns any pending-tail pooled strings that the
    // sealed pool has not, so Pool spans the whole physical range. The store snapshot is immutable, so a concurrent
    // recompute is benign (mirrors EventColumnPool.Prefix).
    private PendingPoolExtension? _pendingPool;

    internal EventColumnStoreReader(EventLogId logId, EventColumnStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        LogId = logId;
        _store = store;
    }

    public long ContentVersion => _store.ContentVersion;

    public int Count => _store.Count;

    public int Generation => _store.Generation;

    public EventLogId LogId { get; }

    public IReadOnlyList<string?> Pool => new PoolView(_store, PendingPool());

    public void BucketTimeTicksByEventData(
        ReadOnlySpan<int> rankByPhysical,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        string fieldName,
        long[] targetCodes,
        int[] slotCounts,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(fieldName);
        ArgumentNullException.ThrowIfNull(targetCodes);
        ArgumentNullException.ThrowIfNull(slotCounts);
        ArgumentOutOfRangeException.ThrowIfNotEqual(rankByPhysical.Length, Count);
        ArgumentOutOfRangeException.ThrowIfLessThan(bucketSpanTicks, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(bucketCount, 1);

        int slotCount = targetCodes.Length + 1;
        ArgumentOutOfRangeException.ThrowIfLessThan(slotCounts.Length, bucketCount * slotCount);

        _store.BucketTimeTicksByEventData(rankByPhysical, minTicks, bucketSpanTicks, bucketCount, fieldName, targetCodes, slotCount, slotCounts, cancellationToken);
    }

    public void BucketTimeTicksByEventDataHResult(
        ReadOnlySpan<int> rankByPhysical,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        string fieldName,
        IReadOnlyCollection<string> eligibleProviders,
        long[] targetCodes,
        int[] slotCounts,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(fieldName);
        ArgumentNullException.ThrowIfNull(eligibleProviders);
        ArgumentNullException.ThrowIfNull(targetCodes);
        ArgumentNullException.ThrowIfNull(slotCounts);
        ArgumentOutOfRangeException.ThrowIfNotEqual(rankByPhysical.Length, Count);
        ArgumentOutOfRangeException.ThrowIfLessThan(bucketSpanTicks, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(bucketCount, 1);

        int slotCount = targetCodes.Length + 1;
        ArgumentOutOfRangeException.ThrowIfLessThan(slotCounts.Length, bucketCount * slotCount);

        _store.BucketTimeTicksByEventDataHResult(rankByPhysical, minTicks, bucketSpanTicks, bucketCount, fieldName, eligibleProviders, targetCodes, slotCount, slotCounts, cancellationToken);
    }

    public void BucketTimeTicksByEventId(
        ReadOnlySpan<int> rankByPhysical,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        int[] targetIds,
        int[] slotCounts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targetIds);
        ArgumentNullException.ThrowIfNull(slotCounts);
        ArgumentOutOfRangeException.ThrowIfNotEqual(rankByPhysical.Length, Count);
        ArgumentOutOfRangeException.ThrowIfLessThan(bucketSpanTicks, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(bucketCount, 1);

        int slotCount = targetIds.Length + 1;
        ArgumentOutOfRangeException.ThrowIfLessThan(slotCounts.Length, bucketCount * slotCount);

        _store.BucketTimeTicksByEventId(rankByPhysical, minTicks, bucketSpanTicks, bucketCount, targetIds, slotCount, slotCounts, cancellationToken);
    }

    public void BucketTimeTicksByField(
        ReadOnlySpan<int> rankByPhysical,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        EventFieldId field,
        string[] targetValues,
        int[] slotCounts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targetValues);
        ArgumentNullException.ThrowIfNull(slotCounts);
        ArgumentOutOfRangeException.ThrowIfNotEqual(rankByPhysical.Length, Count);
        ArgumentOutOfRangeException.ThrowIfLessThan(bucketSpanTicks, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(bucketCount, 1);

        int slotCount = targetValues.Length + 1;
        ArgumentOutOfRangeException.ThrowIfLessThan(slotCounts.Length, bucketCount * slotCount);

        _store.BucketTimeTicksByField(rankByPhysical, minTicks, bucketSpanTicks, bucketCount, ToColumnField(field), targetValues, slotCount, slotCounts, cancellationToken);
    }

    public void BucketTimeTicksBySeverity(
        ReadOnlySpan<int> rankByPhysical,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        int[] slotCounts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(slotCounts);
        ArgumentOutOfRangeException.ThrowIfNotEqual(rankByPhysical.Length, Count);
        ArgumentOutOfRangeException.ThrowIfLessThan(bucketSpanTicks, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(bucketCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(slotCounts.Length, bucketCount * LevelSeverity.SlotCount);

        _store.BucketTimeTicksBySeverity(rankByPhysical, minTicks, bucketSpanTicks, bucketCount, slotCounts, cancellationToken);
    }

    public void CopyGuidColumn(EventFieldId field, Guid[] values, bool[] hasValue)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(hasValue);

        switch (field)
        {
            case EventFieldId.ActivityId:
                _store.CopyActivityId(values, hasValue);
                break;
            case EventFieldId.RelatedActivityId:
                _store.CopyRelatedActivityId(values, hasValue);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field), field, "Not a Guid column.");
        }
    }

    public void CopyInt64Column(EventFieldId field, long[] values, bool[] hasValue)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(hasValue);

        switch (field)
        {
            case EventFieldId.Id:
                _store.CopyId(values, hasValue);
                break;
            case EventFieldId.RecordId:
                _store.CopyRecordId(values, hasValue);
                break;
            case EventFieldId.ProcessId:
                _store.CopyProcessId(values, hasValue);
                break;
            case EventFieldId.ThreadId:
                _store.CopyThreadId(values, hasValue);
                break;
            case EventFieldId.TimeCreated:
                _store.CopyTimeTicks(values, hasValue);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field), field, "Not an integral column.");
        }
    }

    public void CopyPoolIndexColumn(EventFieldId field, int[] poolIndices)
    {
        ArgumentNullException.ThrowIfNull(poolIndices);

        EventColumnField column = ToColumnField(field);
        _store.CopySealedPoolIndex(column, poolIndices);

        if (_store.SealedCount >= _store.Count) { return; }

        PendingPoolExtension extension = PendingPool();

        for (int index = _store.SealedCount; index < _store.Count; index++)
        {
            string? value = PendingPoolString(_store.GetPendingEvent(index), column);
            poolIndices[index] = value is null ? -1 : extension.StorePoolCount + extension.IndexOf(value);
        }
    }

    public void CountEventDataHResults(
        ReadOnlySpan<int> rankByPhysical,
        string fieldName,
        IReadOnlyCollection<string> eligibleProviders,
        IDictionary<long, int> counts,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(fieldName);
        ArgumentNullException.ThrowIfNull(eligibleProviders);
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentOutOfRangeException.ThrowIfNotEqual(rankByPhysical.Length, Count);

        _store.CountEventDataHResults(rankByPhysical, fieldName, eligibleProviders, counts, cancellationToken);
    }

    public void CountEventDataValues(ReadOnlySpan<int> rankByPhysical, string fieldName, IDictionary<long, int> counts, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(fieldName);
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentOutOfRangeException.ThrowIfNotEqual(rankByPhysical.Length, Count);

        _store.CountEventDataValues(rankByPhysical, fieldName, counts, cancellationToken);
    }

    public void CountEventIds(ReadOnlySpan<int> rankByPhysical, IDictionary<int, int> counts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentOutOfRangeException.ThrowIfNotEqual(rankByPhysical.Length, Count);

        _store.CountEventIds(rankByPhysical, counts, cancellationToken);
    }

    public void CountFieldValues(ReadOnlySpan<int> rankByPhysical, EventFieldId field, IDictionary<string, int> counts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentOutOfRangeException.ThrowIfNotEqual(rankByPhysical.Length, Count);

        _store.CountFieldValues(rankByPhysical, ToColumnField(field), counts, cancellationToken);
    }

    public EventDataFieldEnumerator EnumerateEventData(EventLocator locator)
    {
        int index = Resolve(locator);

        return _store.IsPending(index)
            ? new EventDataFieldEnumerator(_store.GetPendingEvent(index).EventData)
            : new EventDataFieldEnumerator(_store, index);
    }

    public UserDataFieldEnumerator EnumerateUserData(EventLocator locator)
    {
        int index = Resolve(locator);

        if (!_store.IsPending(index))
        {
            return new UserDataFieldEnumerator(_store, index);
        }

        ResolvedEvent pending = _store.GetPendingEvent(index);

        return new UserDataFieldEnumerator(pending.UserData, pending.UserDataIncomplete);

    }

    public ResolvedEvent GetDetail(EventLocator locator) => _store.GetDetail(Resolve(locator));

    public ResolvedEvent GetDetailLean(EventLocator locator) => _store.GetDetailLean(Resolve(locator));

    public EventFieldValue GetField(EventLocator locator, EventFieldId field)
    {
        int index = Resolve(locator);

        if (_store.IsPending(index))
        {
            return ResolvedEventFieldReader.GetField(_store.GetPendingEvent(index), field);
        }

        switch (field)
        {
            case EventFieldId.Id:
                return EventFieldValue.FromProperty(_store.RawId(index));
            case EventFieldId.RecordId:
            {
                long value = _store.RawRecordId(index, out bool hasValue);

                return hasValue ? EventFieldValue.FromProperty(value) : ResolvedEventFieldReader.Absent;
            }
            case EventFieldId.Level:
                return PooledField(EventColumnField.Level, index);
            case EventFieldId.TimeCreated:
                return EventFieldValue.FromProperty(new DateTime(_store.RawTimeTicks(index), DateTimeKind.Utc));
            case EventFieldId.ActivityId:
            {
                Guid value = _store.RawActivityId(index, out bool hasValue);

                return hasValue ? EventFieldValue.FromProperty(value) : ResolvedEventFieldReader.Absent;
            }
            case EventFieldId.LogName:
                return PooledField(EventColumnField.LogName, index);
            case EventFieldId.ComputerName:
                return PooledField(EventColumnField.ComputerName, index);
            case EventFieldId.Source:
                return PooledField(EventColumnField.Source, index);
            case EventFieldId.TaskCategory:
                return PooledField(EventColumnField.TaskCategory, index);
            case EventFieldId.KeywordsDisplay:
                return EventFieldValue.FromProperty(JoinKeywords(index));
            case EventFieldId.ProcessId:
            {
                int value = _store.RawProcessId(index, out bool hasValue);

                return hasValue ? EventFieldValue.FromProperty(value) : ResolvedEventFieldReader.Absent;
            }
            case EventFieldId.ThreadId:
            {
                int value = _store.RawThreadId(index, out bool hasValue);

                return hasValue ? EventFieldValue.FromProperty(value) : ResolvedEventFieldReader.Absent;
            }
            case EventFieldId.UserId:
                return UserIdField(index);
            case EventFieldId.Description:
                return PooledField(EventColumnField.Description, index);
            case EventFieldId.Xml:
                return PooledField(EventColumnField.Xml, index);
            case EventFieldId.OwningLog:
                return PooledField(EventColumnField.OwningLog, index);
            case EventFieldId.Opcode:
                return PooledField(EventColumnField.Opcode, index);
            case EventFieldId.RelatedActivityId:
            {
                Guid value = _store.RawRelatedActivityId(index, out bool hasValue);

                return hasValue ? EventFieldValue.FromProperty(value) : ResolvedEventFieldReader.Absent;
            }
            default:
                return ResolvedEventFieldReader.Absent;
        }
    }

    public IReadOnlyList<string> GetKeywords(EventLocator locator)
    {
        int index = Resolve(locator);

        return _store.IsPending(index)
            ? _store.GetPendingEvent(index).Keywords
            : _store.ReconstructStringArray(_store.RawKeywords(index));
    }

    public long GetTimeTicks(EventLocator locator) => _store.RawTimeTicks(Resolve(locator));

    public StructuredFieldResult GetUserData(EventLocator locator, string storageKey)
    {
        int index = Resolve(locator);

        if (_store.IsPending(index))
        {
            return _store.GetPendingEvent(index).TryGetUserDataValues(storageKey);
        }

        int fieldCount = _store.RawUserDataCount(index);

        for (int field = 0; field < fieldCount; field++)
        {
            if (!string.Equals(_store.PoolGet(_store.RawUserDataPathIndex(index, field)), storageKey, StringComparison.Ordinal))
            {
                continue;
            }

            string[] values = _store.ReconstructStringArray(_store.RawUserDataValues(index, field));

            return new StructuredFieldResult(
                EventFieldValue.FromProperty(EventProperty.FromReference(values)),
                _store.RawUserDataTruncated(index, field) || _store.RawUserDataIncomplete(index));
        }

        if (!_store.RawUserDataIncomplete(index))
        {
            return new StructuredFieldResult(EventFieldValue.FromProperty(EventProperty.FromReference(null)), false);
        }

        string[] empty = [];

        return new StructuredFieldResult(
            EventFieldValue.FromProperty(EventProperty.FromReference(empty)),
            isTruncated: true);
    }

    public bool GetUserDataIncomplete(EventLocator locator) => _store.RawUserDataIncomplete(Resolve(locator));

    public EventLocator LocatorAt(int index) => new(LogId, _store.Generation, index);

    public bool TryGetEventData(EventLocator locator, string fieldName, out EventFieldValue value)
    {
        int index = Resolve(locator);

        if (_store.IsPending(index))
        {
            return _store.GetPendingEvent(index).EventData.TryGetValue(fieldName, out value);
        }

        int schemaId = _store.RawEventDataSchemaId(index);

        if (schemaId < 0)
        {
            value = default;

            return false;
        }

        if (TryResolveEventDataIndex(schemaId, fieldName, out int fieldIndex)
            && fieldIndex < _store.RawEventDataCount(index))
        {
            value = EventFieldValue.FromProperty(_store.ReconstructEventProperty(_store.RawEventDataField(index, fieldIndex)));

            return true;
        }

        value = default;

        return false;
    }

    public bool TryGetTimeTicksRange(
        ReadOnlySpan<int> rankByPhysical,
        out long minTicks,
        out long maxTicks,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(rankByPhysical.Length, Count);

        return _store.TryGetTimeTicksRange(rankByPhysical, out minTicks, out maxTicks, cancellationToken);
    }

    private static string? PendingPoolString(ResolvedEvent pending, EventColumnField column) => column switch
    {
        EventColumnField.OwningLog => pending.OwningLog,
        EventColumnField.ComputerName => pending.ComputerName,
        EventColumnField.Description => pending.Description,
        EventColumnField.Level => pending.Level,
        EventColumnField.LogName => pending.LogName,
        EventColumnField.Source => pending.Source,
        EventColumnField.TaskCategory => pending.TaskCategory,
        EventColumnField.Xml => pending.Xml,
        EventColumnField.UserId => pending.UserId?.Value,
        EventColumnField.Opcode => pending.Opcode,
        _ => throw new ArgumentOutOfRangeException(nameof(column), column, null)
    };

    private static EventColumnField ToColumnField(EventFieldId field) => field switch
    {
        EventFieldId.Level => EventColumnField.Level,
        EventFieldId.LogName => EventColumnField.LogName,
        EventFieldId.ComputerName => EventColumnField.ComputerName,
        EventFieldId.Source => EventColumnField.Source,
        EventFieldId.TaskCategory => EventColumnField.TaskCategory,
        EventFieldId.UserId => EventColumnField.UserId,
        EventFieldId.Description => EventColumnField.Description,
        EventFieldId.Xml => EventColumnField.Xml,
        EventFieldId.OwningLog => EventColumnField.OwningLog,
        EventFieldId.Opcode => EventColumnField.Opcode,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Not a single pooled string column.")
    };

    private PendingPoolExtension BuildPendingPool()
    {
        var indexByValue = new Dictionary<string, int>(StringComparer.Ordinal);
        var extras = new List<string>();

        for (int index = _store.SealedCount; index < _store.Count; index++)
        {
            ResolvedEvent pending = _store.GetPendingEvent(index);
            AddPendingValue(pending.OwningLog, indexByValue, extras);
            AddPendingValue(pending.ComputerName, indexByValue, extras);
            AddPendingValue(pending.Description, indexByValue, extras);
            AddPendingValue(pending.Level, indexByValue, extras);
            AddPendingValue(pending.LogName, indexByValue, extras);
            AddPendingValue(pending.Source, indexByValue, extras);
            AddPendingValue(pending.TaskCategory, indexByValue, extras);
            AddPendingValue(pending.Xml, indexByValue, extras);
            AddPendingValue(pending.UserId?.Value, indexByValue, extras);
            AddPendingValue(pending.Opcode, indexByValue, extras);
        }

        return new PendingPoolExtension(_store.PoolDistinctCount, [.. extras], indexByValue);

        static void AddPendingValue(string? value, Dictionary<string, int> indexByValue, List<string> extras)
        {
            if (value is null || indexByValue.ContainsKey(value)) { return; }

            indexByValue[value] = extras.Count;
            extras.Add(value);
        }
    }

    private string JoinKeywords(int index)
    {
        ReadOnlySpan<int> keywordIndices = _store.RawKeywords(index);

        return keywordIndices.Length == 0 ?
            string.Empty :
            string.Join(", ", _store.ReconstructStringArray(keywordIndices));
    }

    private PendingPoolExtension PendingPool()
    {
        PendingPoolExtension? existing = Volatile.Read(ref _pendingPool);

        if (existing is not null) { return existing; }

        PendingPoolExtension built = BuildPendingPool();
        Volatile.Write(ref _pendingPool, built);

        return built;
    }

    private EventFieldValue PooledField(EventColumnField column, int index) =>
        EventFieldValue.FromProperty(_store.PoolGet(_store.RawPoolIndex(column, index)) ?? string.Empty);

    private int Resolve(EventLocator locator)
    {
        if (locator.LogId != LogId || locator.Generation != _store.Generation)
        {
            throw new ArgumentException("Locator does not belong to this reader's log/generation.", nameof(locator));
        }

        return locator.Index;
    }

    private bool TryResolveEventDataIndex(int schemaId, string fieldName, out int fieldIndex)
    {
        ReadOnlySpan<int> nameIndices = _store.SchemaFieldNameIndices(schemaId);

        for (int i = 0; i < nameIndices.Length; i++)
        {
            string? name = _store.PoolGet(nameIndices[i]);

            // First-index-wins over the stored matched-ordering names, skipping positional-only empty nodes, exactly
            // as TemplateFieldSchema.BuildIndex maps names to indices.
            if (!string.IsNullOrEmpty(name) && string.Equals(name, fieldName, StringComparison.Ordinal))
            {
                fieldIndex = i;

                return true;
            }
        }

        fieldIndex = 0;

        return false;
    }

    private EventFieldValue UserIdField(int index)
    {
        int poolIndex = _store.RawPoolIndex(EventColumnField.UserId, index);
        SecurityIdentifier? userId = poolIndex < 0 ? null : new SecurityIdentifier(_store.PoolGet(poolIndex)!);

        return EventFieldValue.FromProperty(userId);
    }

    // The pending-tail pooled strings that the sealed pool does not already index, addressed as
    // (StorePoolCount + extra-index). Extra values may duplicate sealed pool values; that is harmless because callers
    // rank pool strings by value, not by index.
    private sealed class PendingPoolExtension
    {
        private readonly Dictionary<string, int> _indexByValue;

        internal PendingPoolExtension(int storePoolCount, string[] extras, Dictionary<string, int> indexByValue)
        {
            StorePoolCount = storePoolCount;
            Extras = extras;
            _indexByValue = indexByValue;
        }

        internal string[] Extras { get; }

        internal int StorePoolCount { get; }

        internal int IndexOf(string value) => _indexByValue[value];
    }

    private sealed class PoolView : IReadOnlyList<string?>
    {
        private readonly PendingPoolExtension _extension;
        private readonly EventColumnStore _store;

        internal PoolView(EventColumnStore store, PendingPoolExtension extension)
        {
            _store = store;
            _extension = extension;
        }

        public int Count => _extension.StorePoolCount + _extension.Extras.Length;

        public string? this[int index] => index < _extension.StorePoolCount
            ? _store.PoolGet(index)
            : _extension.Extras[index - _extension.StorePoolCount];

        public IEnumerator<string?> GetEnumerator()
        {
            for (int index = 0; index < Count; index++) { yield return this[index]; }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
