// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Security.Principal;

namespace EventLogExpert.Eventing.Models;

public class EventProperties
{
    public Guid? ActivityId { get; set; }

    public string? ComputerName { get; set; }

    public ushort? Id { get; set; }

    public ulong? Keywords { get; set; }

    public byte? Level { get; set; }

    public string? LogName { get; set; }

    public uint? ProcessId { get; set; }

    public ulong? RecordId { get; set; }

    public string? Source { get; set; }

    public ushort? Task { get; set; }

    public uint? ThreadId { get; set; }

    public DateTime? TimeCreated { get; set; }

    public SecurityIdentifier? UserId { get; set; }
}
