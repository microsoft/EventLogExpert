// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Structured;
using System.Security.Principal;

namespace EventLogExpert.Eventing.Common.Events;

internal sealed class EventColumnStoreReader : IEventColumnReader
{
    private readonly EventColumnStore _store;

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

    public EventFieldValue GetField(EventHandle handle, EventFieldId field)
    {
        int index = Resolve(handle);

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
            default:
                return ResolvedEventFieldReader.Absent;
        }
    }

    public StructuredFieldResult GetUserData(EventHandle handle, string storageKey)
    {
        int index = Resolve(handle);

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

    public EventHandle HandleAt(int index) => new(LogId, _store.Generation, index);

    public bool TryGetEventData(EventHandle handle, string fieldName, out EventFieldValue value)
    {
        int index = Resolve(handle);

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

    private string JoinKeywords(int index)
    {
        ReadOnlySpan<int> keywordIndices = _store.RawKeywords(index);

        return keywordIndices.Length == 0 ?
            string.Empty :
            string.Join(", ", _store.ReconstructStringArray(keywordIndices));
    }

    private EventFieldValue PooledField(EventColumnField column, int index) =>
        EventFieldValue.FromProperty(_store.PoolGet(_store.RawPoolIndex(column, index)) ?? string.Empty);

    private int Resolve(EventHandle handle)
    {
        if (handle.LogId != LogId || handle.Generation != _store.Generation)
        {
            throw new ArgumentException("Handle does not belong to this reader's log/generation.", nameof(handle));
        }

        return handle.Index;
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
}
