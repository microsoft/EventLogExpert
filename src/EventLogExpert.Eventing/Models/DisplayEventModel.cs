// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Diagnostics.Eventing.Reader;

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
    IList<EventProperty> Properties,
    int? Qualifiers,
    long? Keywords,
    IEnumerable<string> KeywordsDisplayNames,
    int? ProcessId,
    int? ThreadId,
    string LogName, // This is the log name from the event reader
    string? Template,
    string OwningLog, // This is the name of the log file or the live log, which we use internally
    EventRecord EventRecord)
{
    public EventRecord EventRecord { private get; init; } = EventRecord;

    public string Xml
    {
        get
        {
            if (cachedXml is not null)
            {
                return cachedXml;
            }
            
            lock (this)
            {
                if (cachedXml is null)
                {
                    var unformattedXml = EventRecord.ToXml();
                    cachedXml = System.Xml.Linq.XElement.Parse(unformattedXml).ToString();
                }

                return cachedXml;
            }
        }
    }

    private string? cachedXml = null;
}
