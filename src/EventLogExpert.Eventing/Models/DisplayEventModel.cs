// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using System.Diagnostics.Eventing.Reader;
using System.Security.Principal;

namespace EventLogExpert.Eventing.Models;

public sealed record DisplayEventModel(
    EventRecord EventRecord,
    string OwningLog /*This is the name of the log file or the live log, which we use internally*/)
{
    private EventRecord EventRecord { get; } = EventRecord;

    public Guid? ActivityId => EventRecord.ActivityId;

    public string ComputerName => EventRecord.MachineName;

    public string Description => "";

    public int Id => EventRecord.Id;

    public IEnumerable<string> KeywordsDisplayNames => [];

    // This is the log name from the event reader
    public string Level => Severity.GetString(EventRecord.Level);

    public string LogName => EventRecord.LogName;

    public int? ProcessId => EventRecord.ProcessId;

    public long? RecordId => EventRecord.RecordId;

    public string Source => EventRecord.ProviderName;

    public string TaskCategory => "";

    public int? ThreadId => EventRecord.ThreadId;

    public DateTime TimeCreated => EventRecord.TimeCreated!.Value.ToUniversalTime();

    public SecurityIdentifier UserId => EventRecord.UserId;

    public string Xml => EventRecord.ToXml();
}
