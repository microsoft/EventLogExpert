// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Models;

public sealed record DisplayEventModel(
    long? RecordId,
    Guid? ActivityId,
    DateTime TimeCreated,
    int Id,
    string ComputerName,
    string Level,
    string Source,
    string TaskCategory,
    string Description,
    int? Qualifiers,
    IEnumerable<string> KeywordsDisplayNames,
    int? ProcessId,
    int? ThreadId,
    string LogName, // This is the log name from the event reader
    string OwningLog, // This is the name of the log file or the live log, which we use internally
    string Xml);
