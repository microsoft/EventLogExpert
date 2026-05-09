// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
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

    // This is the log name from the event reader
    public string LogName { get; init; } = string.Empty;

    public int? ProcessId { get; init; }

    public long? RecordId { get; init; }

    public string Source { get; init; } = string.Empty;

    public string TaskCategory { get; init; } = string.Empty;

    public int? ThreadId { get; init; }

    public DateTime TimeCreated { get; init; }

    public SecurityIdentifier? UserId { get; init; }

    /// <summary>
    ///     Pre-rendered XML for the event. Populated by <c>EventLogReader</c> only when the
    ///     log is opened with <c>renderXml: true</c> (currently driven by the presence of an
    ///     applied filter that references this property). When empty, callers should use
    ///     <see cref="EventLogExpert.Eventing.Resolvers.IEventXmlResolver" /> to fetch the XML on demand.
    /// </summary>
    public string Xml { get; init; } = string.Empty;
}
