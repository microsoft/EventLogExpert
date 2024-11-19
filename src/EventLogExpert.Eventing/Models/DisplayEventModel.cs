// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Readers;
using System.Security.Principal;

namespace EventLogExpert.Eventing.Models;

public sealed record DisplayEventModel(
    string OwningLog /*This is the name of the log file or the live log, which we use internally*/,
    PathType PathType)
{
    public Guid? ActivityId { get; init; }

    public string ComputerName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public int Id { get; init; }

    public IEnumerable<string> KeywordsDisplayNames { get; init; } = [];

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

    public string Xml
    {
        get
        {
            using EvtHandle handle = EventMethods.EvtQuery(
                EventLogSession.GlobalSession.Handle,
                OwningLog,
                $"*[System/EventRecordID=\"{RecordId}\"]",
                PathType);

            if (handle.IsInvalid) { return string.Empty; }

            var buffer = new IntPtr[1];
            int count = 0;

            bool success = EventMethods.EvtNext(handle, buffer.Length, buffer, 0, 0, ref count);

            if (!success) { return string.Empty; }

            using var eventHandle = new EvtHandle(buffer[0]);

            if (eventHandle.IsInvalid) { return string.Empty; }

            return EventMethods.RenderEventXml(eventHandle) ?? string.Empty;
        }
    }
}
