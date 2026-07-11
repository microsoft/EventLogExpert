// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
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

    public StructuredFieldResult GetUserData(EventLocator locator, string storageKey) =>
        GetEvent(locator).TryGetUserDataValues(storageKey);

    public bool GetUserDataIncomplete(EventLocator locator) => GetEvent(locator).UserDataIncomplete;

    public EventLocator LocatorAt(int index) => new(LogId, Generation, index);

    public bool TryGetEventData(EventLocator locator, string fieldName, out EventFieldValue value) =>
        GetEvent(locator).EventData.TryGetValue(fieldName, out value);

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
