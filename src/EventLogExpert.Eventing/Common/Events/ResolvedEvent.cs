// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Resolvers;
using System.Collections.Immutable;
using System.Security.Principal;

namespace EventLogExpert.Eventing.Common.Events;

public sealed record ResolvedEvent(
    string OwningLog /*This is the name of the log file or the live log, which we use internally*/,
    LogPathType LogPathType)
{
    public Guid? ActivityId { get; init; }

    public string ComputerName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public int Id { get; init; }

    public IReadOnlyList<string> Keywords { get; init; } = [];

    public string KeywordsDisplayName => Keywords.Count == 0 ? string.Empty : string.Join(", ", Keywords);

    public string Level { get; init; } = string.Empty;

    // The event's Channel system property (e.g. "Security"), populated for .evtx files as the original channel, not the file path.
    public string LogName { get; init; } = string.Empty;

    public int? ProcessId { get; init; }

    public long? RecordId { get; init; }

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
    ///     Named &lt;EventData&gt; fields for this event, or <see cref="EventDataKind.None" /> for legacy / template-less
    ///     events. The underlying values also remain reachable positionally through the description and XML.
    /// </summary>
    public EventDataView EventData => EventDataValues.IsDefaultOrEmpty
        ? EventDataView.Empty
        : new(EventDataValues, EventDataSchema);
}
