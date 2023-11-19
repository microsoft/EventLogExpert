// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Models;

public sealed class EventModel
{
    public long Id { get; set; }

    public byte Version { get; set; }

    public string? LogName { get; set; }

    public int Level { get; set; }

    public int Opcode { get; set; }

    public int Task { get; set; }

    public long[] Keywords { get; set; } = null!;

    public string? Template { get; set; }

    public string? Description { get; set; }
}
