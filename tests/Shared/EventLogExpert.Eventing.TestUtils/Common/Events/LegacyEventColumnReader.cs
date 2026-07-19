// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Structured;

namespace EventLogExpert.Eventing.Common.Events;

public sealed class LegacyEventColumnReader : IEventColumnReader
{
    private readonly IReadOnlyList<ResolvedEvent> _events;

    // Lazily built union of the pooled string columns' distinct values, so the AoS bridge can answer the same bulk
    // pool-index API as the column store (correctness only; this reader is never on the live path).
    private LegacyStringPool? _pool;

    public LegacyEventColumnReader(EventLogId logId, int generation, long contentVersion, IReadOnlyList<ResolvedEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        LogId = logId;
        Generation = generation;
        ContentVersion = contentVersion;
        _events = events;
    }

    public long ContentVersion { get; }

    public int Count => _events.Count;

    public IReadOnlyList<ResolvedEvent> Events => _events;

    public int Generation { get; }

    public EventLogId LogId { get; }

    public IReadOnlyList<string?> Pool => StringPool().Values;

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

        int slotCount = targetCodes.Length + 1;
        int otherSlot = targetCodes.Length;

        for (int index = 0; index < _events.Count; index++)
        {
            if (rankByPhysical[index] < 0) { continue; }

            long bucket = (_events[index].TimeCreated.Ticks - minTicks) / bucketSpanTicks;
            int clamped = bucket < 0 ? 0 : bucket >= bucketCount ? bucketCount - 1 : (int)bucket;
            int slot = otherSlot;

            if (_events[index].EventData.TryGetValue(fieldName, out EventFieldValue value) && value.TryGetWholeNumber(out long code))
            {
                for (int target = 0; target < targetCodes.Length; target++)
                {
                    if (targetCodes[target] == code)
                    {
                        slot = target;

                        break;
                    }
                }
            }

            slotCounts[(clamped * slotCount) + slot]++;
        }
    }

    public void BucketTimeTicksByEventDataHResult(
        ReadOnlySpan<int> rankByPhysical,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        string fieldName,
        IReadOnlyCollection<string> eligibleProviders,
        IReadOnlyList<string> userDataErrorCodePaths,
        long[] targetCodes,
        int[] slotCounts,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(fieldName);
        ArgumentNullException.ThrowIfNull(eligibleProviders);
        ArgumentNullException.ThrowIfNull(userDataErrorCodePaths);
        ArgumentNullException.ThrowIfNull(targetCodes);
        ArgumentNullException.ThrowIfNull(slotCounts);
        ArgumentOutOfRangeException.ThrowIfNotEqual(rankByPhysical.Length, Count);

        int slotCount = targetCodes.Length + 1;
        int otherSlot = targetCodes.Length;
        IReadOnlySet<string> eligibleNames = AsOrdinalSet(eligibleProviders);

        for (int index = 0; index < _events.Count; index++)
        {
            if (rankByPhysical[index] < 0) { continue; }

            ResolvedEvent resolved = _events[index];

            if (!eligibleNames.Contains(resolved.Source)) { continue; }

            if (!TryGetHResult(resolved, fieldName, userDataErrorCodePaths, out long code)) { continue; }

            long bucket = (resolved.TimeCreated.Ticks - minTicks) / bucketSpanTicks;
            int clamped = bucket < 0 ? 0 : bucket >= bucketCount ? bucketCount - 1 : (int)bucket;
            int slot = otherSlot;

            for (int target = 0; target < targetCodes.Length; target++)
            {
                if (targetCodes[target] == code) { slot = target; break; }
            }

            slotCounts[(clamped * slotCount) + slot]++;
        }
    }

    public void BucketTimeTicksByEventDataString(
        ReadOnlySpan<int> rankByPhysical,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        string[] candidateFields,
        IReadOnlyDictionary<string, int> rawValueToSlot,
        int slotCount,
        int[] slotCounts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidateFields);
        ArgumentNullException.ThrowIfNull(rawValueToSlot);
        ArgumentNullException.ThrowIfNull(slotCounts);
        ArgumentOutOfRangeException.ThrowIfNotEqual(rankByPhysical.Length, Count);

        int otherSlot = slotCount - 1;

        for (int index = 0; index < _events.Count; index++)
        {
            if (rankByPhysical[index] < 0) { continue; }

            long bucket = (_events[index].TimeCreated.Ticks - minTicks) / bucketSpanTicks;
            int clamped = bucket < 0 ? 0 : bucket >= bucketCount ? bucketCount - 1 : (int)bucket;
            int slot = otherSlot;

            for (int candidate = 0; candidate < candidateFields.Length; candidate++)
            {
                if (_events[index].EventData.TryGetRawValue(candidateFields[candidate], out EventProperty property)
                    && property.Reference is string raw
                    && EventColumnStore.IsUsableRawValue(raw))
                {
                    slot = rawValueToSlot.TryGetValue(raw, out int mapped) ? mapped : otherSlot;

                    break;
                }
            }

            slotCounts[(clamped * slotCount) + slot]++;
        }
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

        int slotCount = targetIds.Length + 1;
        int otherSlot = targetIds.Length;

        for (int index = 0; index < _events.Count; index++)
        {
            if (rankByPhysical[index] < 0) { continue; }

            long bucket = (_events[index].TimeCreated.Ticks - minTicks) / bucketSpanTicks;
            int clamped = bucket < 0 ? 0 : bucket >= bucketCount ? bucketCount - 1 : (int)bucket;
            int slot = otherSlot;

            for (int target = 0; target < targetIds.Length; target++)
            {
                if (_events[index].Id == targetIds[target]) { slot = target; break; }
            }

            slotCounts[(clamped * slotCount) + slot]++;
        }
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

        int slotCount = targetValues.Length + 1;
        int otherSlot = targetValues.Length;

        for (int index = 0; index < _events.Count; index++)
        {
            if (rankByPhysical[index] < 0) { continue; }

            long bucket = (_events[index].TimeCreated.Ticks - minTicks) / bucketSpanTicks;
            int clamped = bucket < 0 ? 0 : bucket >= bucketCount ? bucketCount - 1 : (int)bucket;
            int slot = SlotForString(FieldValue(_events[index], field), targetValues, otherSlot);
            slotCounts[(clamped * slotCount) + slot]++;
        }
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

        int slotCount = LevelSeverity.SlotCount;

        for (int index = 0; index < _events.Count; index++)
        {
            if (rankByPhysical[index] < 0) { continue; }

            long bucket = (_events[index].TimeCreated.Ticks - minTicks) / bucketSpanTicks;
            int clamped = bucket < 0 ? 0 : bucket >= bucketCount ? bucketCount - 1 : (int)bucket;
            int slot = LevelSeverity.Slot(LevelSeverity.FromLevelName(_events[index].Level));
            slotCounts[(clamped * slotCount) + slot]++;
        }
    }

    public void CopyGuidColumn(EventFieldId field, Guid[] values, bool[] hasValue)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(hasValue);

        switch (field)
        {
            case EventFieldId.ActivityId:
                for (int index = 0; index < _events.Count; index++)
                {
                    Guid? value = _events[index].ActivityId;
                    values[index] = value ?? Guid.Empty;
                    hasValue[index] = value.HasValue;
                }

                break;
            case EventFieldId.RelatedActivityId:
                for (int index = 0; index < _events.Count; index++)
                {
                    Guid? value = _events[index].RelatedActivityId;
                    values[index] = value ?? Guid.Empty;
                    hasValue[index] = value.HasValue;
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field), field, "Not a Guid column.");
        }
    }

    public void CopyInt64Column(EventFieldId field, long[] values, bool[] hasValue)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(hasValue);

        for (int index = 0; index < _events.Count; index++)
        {
            ResolvedEvent resolvedEvent = _events[index];

            switch (field)
            {
                case EventFieldId.Id:
                    values[index] = resolvedEvent.Id;
                    hasValue[index] = true;
                    break;
                case EventFieldId.RecordId:
                    values[index] = resolvedEvent.RecordId ?? 0;
                    hasValue[index] = resolvedEvent.RecordId.HasValue;
                    break;
                case EventFieldId.ProcessId:
                    values[index] = resolvedEvent.ProcessId ?? 0;
                    hasValue[index] = resolvedEvent.ProcessId.HasValue;
                    break;
                case EventFieldId.ThreadId:
                    values[index] = resolvedEvent.ThreadId ?? 0;
                    hasValue[index] = resolvedEvent.ThreadId.HasValue;
                    break;
                case EventFieldId.TimeCreated:
                    values[index] = resolvedEvent.TimeCreated.Ticks;
                    hasValue[index] = true;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(field), field, "Not an integral column.");
            }
        }
    }

    public void CopyPoolIndexColumn(EventFieldId field, int[] poolIndices)
    {
        ArgumentNullException.ThrowIfNull(poolIndices);

        LegacyStringPool pool = StringPool();

        for (int index = 0; index < _events.Count; index++)
        {
            string? value = RawPoolString(_events[index], field);
            poolIndices[index] = value is null ? -1 : pool.IndexOf(value);
        }
    }

    public void CountEventDataHResults(ReadOnlySpan<int> rankByPhysical, string fieldName, IReadOnlyCollection<string> eligibleProviders, IReadOnlyList<string> userDataErrorCodePaths, IDictionary<long, int> counts, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(fieldName);
        ArgumentNullException.ThrowIfNull(eligibleProviders);
        ArgumentNullException.ThrowIfNull(userDataErrorCodePaths);
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentOutOfRangeException.ThrowIfNotEqual(rankByPhysical.Length, Count);

        IReadOnlySet<string> eligibleNames = AsOrdinalSet(eligibleProviders);

        for (int index = 0; index < _events.Count; index++)
        {
            if (rankByPhysical[index] < 0) { continue; }

            ResolvedEvent resolved = _events[index];

            if (!eligibleNames.Contains(resolved.Source)) { continue; }

            if (TryGetHResult(resolved, fieldName, userDataErrorCodePaths, out long code))
            {
                counts[code] = counts.TryGetValue(code, out int existing) ? existing + 1 : 1;
            }
        }
    }

    public void CountEventDataStringValues(ReadOnlySpan<int> rankByPhysical, string[] candidateFields, IDictionary<string, int> counts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidateFields);
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentOutOfRangeException.ThrowIfNotEqual(rankByPhysical.Length, Count);

        for (int index = 0; index < _events.Count; index++)
        {
            if (rankByPhysical[index] < 0) { continue; }

            for (int candidate = 0; candidate < candidateFields.Length; candidate++)
            {
                if (_events[index].EventData.TryGetRawValue(candidateFields[candidate], out EventProperty property)
                    && property.Reference is string raw
                    && EventColumnStore.IsUsableRawValue(raw))
                {
                    counts[raw] = counts.TryGetValue(raw, out int existing) ? existing + 1 : 1;

                    break;
                }
            }
        }
    }

    public void CountEventDataValues(ReadOnlySpan<int> rankByPhysical, string fieldName, IDictionary<long, int> counts, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(fieldName);
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentOutOfRangeException.ThrowIfNotEqual(rankByPhysical.Length, Count);

        for (int index = 0; index < _events.Count; index++)
        {
            if (rankByPhysical[index] < 0) { continue; }

            if (_events[index].EventData.TryGetValue(fieldName, out EventFieldValue value) && value.TryGetWholeNumber(out long code))
            {
                counts[code] = counts.TryGetValue(code, out int existing) ? existing + 1 : 1;
            }
        }
    }

    public void CountEventIds(ReadOnlySpan<int> rankByPhysical, IDictionary<int, int> counts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentOutOfRangeException.ThrowIfNotEqual(rankByPhysical.Length, Count);

        for (int index = 0; index < _events.Count; index++)
        {
            if (rankByPhysical[index] < 0) { continue; }

            int id = _events[index].Id;
            counts[id] = counts.TryGetValue(id, out int existing) ? existing + 1 : 1;
        }
    }

    public void CountFieldValues(ReadOnlySpan<int> rankByPhysical, EventFieldId field, IDictionary<string, int> counts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentOutOfRangeException.ThrowIfNotEqual(rankByPhysical.Length, Count);

        for (int index = 0; index < _events.Count; index++)
        {
            if (rankByPhysical[index] < 0) { continue; }

            string? value = FieldValue(_events[index], field);

            if (string.IsNullOrEmpty(value)) { continue; }

            counts[value] = counts.TryGetValue(value, out int existing) ? existing + 1 : 1;
        }
    }

    public EventDataFieldEnumerator EnumerateEventData(EventLocator locator) => new(GetEvent(locator).EventData);

    public UserDataFieldEnumerator EnumerateUserData(EventLocator locator)
    {
        ResolvedEvent resolvedEvent = GetEvent(locator);

        return new UserDataFieldEnumerator(resolvedEvent.UserData, resolvedEvent.UserDataIncomplete);
    }

    public ResolvedEvent GetDetail(EventLocator locator) => GetEvent(locator);

    public ResolvedEvent GetDetailLean(EventLocator locator) => GetEvent(locator);

    // Legacy-adapter convenience: the current store keeps whole ResolvedEvent objects, so a locator resolves directly to
    // one. The real column store will not retain events; its view rehydrates from columns instead.
    public ResolvedEvent GetEvent(EventLocator locator)
    {
        if (locator.LogId != LogId || locator.Generation != Generation)
        {
            throw new ArgumentException("Locator does not belong to this reader's log/generation.", nameof(locator));
        }

        return _events[locator.Index];
    }

    public EventFieldValue GetField(EventLocator locator, EventFieldId field) =>
        ResolvedEventFieldReader.GetField(GetEvent(locator), field);

    public IReadOnlyList<string> GetKeywords(EventLocator locator) => GetEvent(locator).Keywords;

    public long GetTimeTicks(EventLocator locator) => GetEvent(locator).TimeCreated.Ticks;

    public StructuredFieldResult GetUserData(EventLocator locator, string storageKey) =>
        GetEvent(locator).TryGetUserDataValues(storageKey);

    public bool GetUserDataIncomplete(EventLocator locator) => GetEvent(locator).UserDataIncomplete;

    public EventLocator LocatorAt(int index) => new(LogId, Generation, index);

    public bool TryGetEventData(EventLocator locator, string fieldName, out EventFieldValue value) =>
        GetEvent(locator).EventData.TryGetValue(fieldName, out value);

    public bool TryGetTimeTicksRange(
        ReadOnlySpan<int> rankByPhysical,
        out long minTicks,
        out long maxTicks,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(rankByPhysical.Length, Count);

        long min = long.MaxValue;
        long max = long.MinValue;
        bool any = false;

        for (int index = 0; index < _events.Count; index++)
        {
            if (rankByPhysical[index] < 0) { continue; }

            long ticks = _events[index].TimeCreated.Ticks;
            if (ticks < min) { min = ticks; }
            if (ticks > max) { max = ticks; }
            any = true;
        }

        minTicks = any ? min : 0;
        maxTicks = any ? max : 0;

        return any;
    }

    private static HashSet<string> AsOrdinalSet(IReadOnlyCollection<string> providers) => new(providers, StringComparer.Ordinal);

    private static bool ContainsOrdinal(IReadOnlyList<string> paths, string value)
    {
        for (int index = 0; index < paths.Count; index++)
        {
            if (string.Equals(paths[index], value, StringComparison.Ordinal)) { return true; }
        }

        return false;
    }

    private static string? FieldValue(ResolvedEvent @event, EventFieldId field) => field switch
    {
        EventFieldId.Source => @event.Source,
        EventFieldId.TaskCategory => @event.TaskCategory,
        EventFieldId.Opcode => @event.Opcode,
        EventFieldId.LogName => @event.LogName,
        EventFieldId.ComputerName => @event.ComputerName,
        EventFieldId.OwningLog => @event.OwningLog,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Field is not a supported group-by dimension.")
    };

    private static string? RawPoolString(ResolvedEvent resolvedEvent, EventFieldId field) => field switch
    {
        EventFieldId.Level => resolvedEvent.Level,
        EventFieldId.LogName => resolvedEvent.LogName,
        EventFieldId.ComputerName => resolvedEvent.ComputerName,
        EventFieldId.Source => resolvedEvent.Source,
        EventFieldId.TaskCategory => resolvedEvent.TaskCategory,
        EventFieldId.UserId => resolvedEvent.UserId?.Value,
        EventFieldId.Description => resolvedEvent.Description,
        EventFieldId.Xml => resolvedEvent.Xml,
        EventFieldId.OwningLog => resolvedEvent.OwningLog,
        EventFieldId.Opcode => resolvedEvent.Opcode,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Not a single pooled string column.")
    };

    private static int SlotForString(string? value, string[] targets, int otherSlot)
    {
        for (int slot = 0; slot < targets.Length; slot++)
        {
            if (string.Equals(value, targets[slot], StringComparison.Ordinal)) { return slot; }
        }

        return otherSlot;
    }

    // Mirrors EventColumnStore: an eligible row's failure HRESULT comes from its EventData errorCode field, or - when that
    // field is absent (servicing rows store no EventData) - from a curated UserData Cbs*/ErrorCode leaf. 0x0 and empty
    // parse to a zero/no code and are dropped like an absent field, so this oracle omits the same rows the store does.
    private static bool TryGetHResult(ResolvedEvent resolved, string fieldName, IReadOnlyList<string> userDataErrorCodePaths, out long code)
    {
        if (resolved.EventData.TryGetValue(fieldName, out EventFieldValue value) && value.TryGetHResult32(out uint hresult) && hresult != 0)
        {
            code = hresult;

            return true;
        }

        if (!resolved.UserData.IsDefaultOrEmpty)
        {
            foreach (UserDataField field in resolved.UserData)
            {
                if (!ContainsOrdinal(userDataErrorCodePaths, field.Path)) { continue; }

                if (!field.Values.IsDefaultOrEmpty && EventFieldValue.TryParseHResult32(field.Values[0], out uint userDataHresult) && userDataHresult != 0)
                {
                    code = userDataHresult;

                    return true;
                }
            }
        }

        code = 0;

        return false;
    }

    private LegacyStringPool BuildStringPool()
    {
        var indexByValue = new Dictionary<string, int>(StringComparer.Ordinal);
        var values = new List<string?>();

        foreach (ResolvedEvent resolvedEvent in _events)
        {
            Add(resolvedEvent.Level, indexByValue, values);
            Add(resolvedEvent.LogName, indexByValue, values);
            Add(resolvedEvent.ComputerName, indexByValue, values);
            Add(resolvedEvent.Source, indexByValue, values);
            Add(resolvedEvent.TaskCategory, indexByValue, values);
            Add(resolvedEvent.UserId?.Value, indexByValue, values);
            Add(resolvedEvent.Description, indexByValue, values);
            Add(resolvedEvent.Xml, indexByValue, values);
            Add(resolvedEvent.OwningLog, indexByValue, values);
            Add(resolvedEvent.Opcode, indexByValue, values);
        }

        return new LegacyStringPool(values, indexByValue);

        static void Add(string? value, Dictionary<string, int> indexByValue, List<string?> values)
        {
            if (value is null || indexByValue.ContainsKey(value)) { return; }

            indexByValue[value] = values.Count;
            values.Add(value);
        }
    }

    private LegacyStringPool StringPool()
    {
        LegacyStringPool? existing = Volatile.Read(ref _pool);

        if (existing is not null) { return existing; }

        LegacyStringPool built = BuildStringPool();
        Volatile.Write(ref _pool, built);

        return built;
    }

    private sealed class LegacyStringPool
    {
        private readonly Dictionary<string, int> _indexByValue;

        internal LegacyStringPool(IReadOnlyList<string?> values, Dictionary<string, int> indexByValue)
        {
            Values = values;
            _indexByValue = indexByValue;
        }

        internal IReadOnlyList<string?> Values { get; }

        internal int IndexOf(string value) => _indexByValue[value];
    }
}
