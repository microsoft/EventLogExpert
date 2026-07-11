// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Eventing.Structured;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace EventLogExpert.Eventing.Common.Events;

public sealed record ResolvedEvent(
    string OwningLog /*This is the name of the log file or the live log, which we use internally*/,
    LogPathType LogPathType)
{
    private static readonly StructuredFieldResult s_absentUserData =
        new(EventFieldValue.FromProperty(EventProperty.FromReference(null)), false);

    public Guid? ActivityId { get; init; }

    public string ComputerName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public int Id { get; init; }

    public IReadOnlyList<string> Keywords { get; init; } = [];

    public string KeywordsDisplayName => Keywords.Count == 0 ? string.Empty : string.Join(", ", Keywords);

    public string Level { get; init; } = string.Empty;

    // The event's Channel system property (e.g. "Security"), populated for .evtx files as the original channel, not the file path.
    public string LogName { get; init; } = string.Empty;

    // The resolved opcode display name (e.g. "Info", "Start"), like TaskCategory; empty when the event carries no opcode.
    public string Opcode { get; init; } = string.Empty;

    public int? ProcessId { get; init; }

    public long? RecordId { get; init; }

    public Guid? RelatedActivityId { get; init; }

    public string Source { get; init; } = string.Empty;

    public string TaskCategory { get; init; } = string.Empty;

    public int? ThreadId { get; init; }

    public DateTime TimeCreated { get; init; }

    public SecurityIdentifier? UserId { get; init; }

    /// <summary>
    ///     Pre-rendered XML for the event. Populated by <c>EventLogReader</c> only when the log is opened with
    ///     <c>renderXml: true</c> (currently driven by the presence of an applied filter that references this property). When
    ///     empty, callers should use <see cref="EventLogExpert.Eventing.Resolvers.IEventXmlResolver" /> to fetch the XML on
    ///     demand.
    /// </summary>
    public string Xml { get; init; } = string.Empty;

    internal ImmutableArray<EventProperty> EventDataValues { get; init; }

    internal TemplateFieldSchema? EventDataSchema { get; init; }

    /// <summary>
    ///     Deduped nested-UserData fields extracted once at resolve time (flat UserData surfaces as
    ///     <see cref="EventData" /> instead). Each field's <see cref="UserDataField.Path" /> is a storage key; repeats
    ///     collapse into one multi-value field. <c>default</c> / empty when the event has no nested UserData.
    /// </summary>
    public ImmutableArray<UserDataField> UserData { get; init; }

    /// <summary>
    ///     <c>true</c> when extraction hit the per-event distinct-path cap, so a path absent from <see cref="UserData" />
    ///     may still have existed. <see cref="TryGetUserDataValues" /> then returns a keep-visible (truncated) result rather
    ///     than a decisive no-match.
    /// </summary>
    public bool UserDataIncomplete { get; init; }

    /// <summary>
    ///     Named &lt;EventData&gt; fields for this event, or <see cref="EventDataKind.None" /> for legacy / template-less
    ///     events. The underlying values also remain reachable positionally through the description and XML.
    /// </summary>
    public EventDataView EventData => EventDataValues.IsDefaultOrEmpty
        ? EventDataView.Empty
        : new(EventDataValues, EventDataSchema);

    /// <summary>
    ///     Looks up a stored UserData field by its <paramref name="storageKey" /> and returns its values, or an absent
    ///     result when none was stored. When <see cref="UserDataIncomplete" /> is set, an absent field instead yields a
    ///     present-but-empty truncated result so a filter keeps the row visible (Unknown) rather than a wrong no-match.
    /// </summary>
    public StructuredFieldResult TryGetUserDataValues(string storageKey)
    {
        if (!UserData.IsDefaultOrEmpty)
        {
            foreach (UserDataField field in UserData)
            {
                if (!string.Equals(field.Path, storageKey, StringComparison.Ordinal)) { continue; }

                string[] values = ImmutableCollectionsMarshal.AsArray(field.Values) ?? [];

                return new StructuredFieldResult(
                    EventFieldValue.FromProperty(EventProperty.FromReference(values)),
                    field.IsTruncated || UserDataIncomplete);
            }
        }

        if (!UserDataIncomplete) { return s_absentUserData; }

        string[] empty = [];

        return new StructuredFieldResult(
            EventFieldValue.FromProperty(EventProperty.FromReference(empty)),
            isTruncated: true);

    }
}
