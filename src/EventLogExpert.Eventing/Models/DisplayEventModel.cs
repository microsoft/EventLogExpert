// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Diagnostics.Eventing.Reader;
using System.Xml.Linq;

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
    EventRecord EventRecord)
{
    private EventRecord? EventRecord { get; set; } = EventRecord;

    public string Xml
    {
        get
        {
            if (_cachedXml is not null) { return _cachedXml; }

            lock (this)
            {
                if (_cachedXml is not null) { return _cachedXml; }

                if (EventRecord is null)
                {
                    return "Unable to get XML. EventRecord is null.";
                }

                var unformattedXml = EventRecord.ToXml();

                try
                {
                    _cachedXml = XElement.Parse(unformattedXml).ToString();
                }
                catch
                {
                    _cachedXml = unformattedXml;
                }

                EventRecord = null;

                return _cachedXml;
            }
        }
    }

    private string? _cachedXml;
}
