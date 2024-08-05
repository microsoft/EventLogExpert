// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Security.Principal;

namespace EventLogExpert.Eventing.Models;

public sealed record DisplayEventModel(
    string OwningLog /*This is the name of the log file or the live log, which we use internally*/)
{
    public Guid? ActivityId { get; init; }

    public string ComputerName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public int Id { get; init; }

    public IEnumerable<string> KeywordsDisplayNames { get; init; } = [];

    // This is the log name from the event reader
    public string Level { get; init; } = string.Empty;

    public string LogName { get; init; } = string.Empty;

    public int? ProcessId { get; init; }

    public long? RecordId { get; init; }

    public string Source { get; init; } = string.Empty;

    public string TaskCategory { get; init; } = string.Empty;

    public int? ThreadId { get; init; }

    public DateTime TimeCreated { get; init; }

    public SecurityIdentifier UserId { get; init; } = new(WellKnownSidType.NullSid, null);

    public string Xml { get; init; } = "Unable to load XML, ensure that loading XML data is enabled in the settings.";
}
