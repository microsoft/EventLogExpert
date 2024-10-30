// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Security.Principal;

namespace EventLogExpert.Eventing.Models;

public record struct EventRecord
{
    public Guid? ActivityId { get; set; }

    public string ComputerName { get; set; }

    public ushort Id { get; set; }

    public long? Keywords { get; set; }

    public byte? Level { get; set; }

    public string LogName { get; set; }

    public int? ProcessId { get; set; }

    public long? RecordId { get; set; }

    public IList<object> Properties { get; set; }

    public string ProviderName { get; set; }

    public ushort? Task { get; set; }

    public int? ThreadId { get; set; }

    public DateTime TimeCreated { get; set; }

    public SecurityIdentifier? UserId { get; set; }

    public byte? Version { get; set; }

    public string? Xml { get; set; }
}
