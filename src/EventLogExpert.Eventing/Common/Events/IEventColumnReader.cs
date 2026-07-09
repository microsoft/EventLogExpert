// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Structured;

namespace EventLogExpert.Eventing.Common.Events;

public interface IEventColumnReader
{
    long ContentVersion { get; }

    int Count { get; }

    int Generation { get; }

    EventLogId LogId { get; }

    /// <summary>
    ///     The interned string pool backing the pooled columns, for ranking distinct <see cref="CopyPoolIndexColumn" />
    ///     values (index i is the string at pool index i; may contain duplicate values across appended segments).
    /// </summary>
    IReadOnlyList<string?> Pool { get; }

    /// <summary>
    ///     Bulk-copies the <see cref="EventFieldId.ActivityId" /> column into caller-owned arrays over the physical range
    ///     [0, <see cref="Count" />). <paramref name="hasValue" />[i] is false for an absent (null) value.
    /// </summary>
    void CopyGuidColumn(EventFieldId field, Guid[] values, bool[] hasValue);

    /// <summary>
    ///     Bulk-copies an integral column (Id, RecordId, ProcessId, ThreadId as values; TimeCreated as UTC ticks) into
    ///     caller-owned arrays over [0, <see cref="Count" />). ProcessId and ThreadId widen from int. Id and TimeCreated are
    ///     always present; <paramref name="hasValue" />[i] is false only for an absent RecordId/ProcessId/ThreadId.
    /// </summary>
    void CopyInt64Column(EventFieldId field, long[] values, bool[] hasValue);

    /// <summary>
    ///     Bulk-copies a pooled string column (Level, LogName, ComputerName, Source, TaskCategory, UserId, Description,
    ///     Xml, OwningLog) as raw pool indices over [0, <see cref="Count" />); -1 marks a null value, and each non-negative
    ///     entry indexes <see cref="Pool" />. KeywordsDisplay is not a single pooled column and is not supported here.
    /// </summary>
    void CopyPoolIndexColumn(EventFieldId field, int[] poolIndices);

    EventDataFieldEnumerator EnumerateEventData(EventLocator locator);

    UserDataFieldEnumerator EnumerateUserData(EventLocator locator);

    /// <summary>
    ///     Rehydrates the full <see cref="ResolvedEvent" /> addressed by <paramref name="locator" /> (every field,
    ///     including the detail-only &lt;EventData&gt;, UserData, and XML), letting a caller re-materialize a row without
    ///     reaching into the underlying store.
    /// </summary>
    /// <param name="locator">The row to rehydrate, as minted by this reader for its own snapshot.</param>
    /// <returns>The fully reconstructed event.</returns>
    ResolvedEvent GetDetail(EventLocator locator);

    /// <summary>
    ///     The viewport variant of <see cref="GetDetail" />: rehydrates the grid-visible fields of the row addressed by
    ///     <paramref name="locator" /> and leaves the detail-only payloads unmaterialized, for the fast display path.
    /// </summary>
    /// <param name="locator">The row to rehydrate, as minted by this reader for its own snapshot.</param>
    /// <returns>The event with grid fields populated and detail-only payloads left empty.</returns>
    ResolvedEvent GetDetailLean(EventLocator locator);

    EventFieldValue GetField(EventLocator locator, EventFieldId field);

    IReadOnlyList<string> GetKeywords(EventLocator locator);

    StructuredFieldResult GetUserData(EventLocator locator, string storageKey);

    bool GetUserDataIncomplete(EventLocator locator);

    EventLocator LocatorAt(int index);

    bool TryGetEventData(EventLocator locator, string fieldName, out EventFieldValue value);
}
