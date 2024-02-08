// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Diagnostics.Eventing.Reader;

namespace EventLogExpert.Eventing.Models;

public sealed class DisplayEventModel
{
    public Guid? ActivityId { get; }
    public string ComputerName { get; }
    public string Description { get; set; }
    public int Id { get; }
    public IEnumerable<string> KeywordsDisplayNames { get; }
    public string Level { get; }
    public string LogName { get; }
    public string OwningLog { get; }
    public int? ProcessId { get; }
    public int? Qualifiers { get; }
    public long? RecordId { get; }
    public string Source { get; }
    public string TaskCategory { get; }
    public int? ThreadId { get; }
    public DateTime TimeCreated { get; }

    public DisplayEventModel(
    long? recordId,
    Guid? activityId,
    DateTime timeCreated,
    int id,
    string computerName,
    string level,
    string source,
    string taskCategory,
    string description,
    int? qualifiers,
    IEnumerable<string> keywordsDisplayNames,
    int? processId,
    int? threadId,
    string logName, // This is the log name from the event reader
    string owningLog, // This is the name of the log file or the live log, which we use internally
    EventRecord eventRecord)
    {
        // Public immutable properties
        ActivityId = activityId;
        ComputerName = computerName;
        Id = id;
        KeywordsDisplayNames = keywordsDisplayNames;
        Level = level;
        LogName = logName;
        OwningLog = owningLog;
        ProcessId = processId;
        Qualifiers = qualifiers;
        RecordId = recordId;
        Source = source;
        TaskCategory = taskCategory;
        ThreadId = threadId;
        TimeCreated = timeCreated;

        // Public mutable properties
        Description = description;

        // Private properties
        _eventRecord = eventRecord;
    }

    public string Xml
    {
        get
        {
            if (_cachedXml is not null)
            {
                return _cachedXml;
            }
            
            lock (this)
            {
                if (_cachedXml is null)
                {
                    if (_eventRecord is null)
                    {
                        return "Unable to get XML. EventRecord is null.";
                    }

                    var unformattedXml = _eventRecord.ToXml();
                    try
                    {
                        _cachedXml = System.Xml.Linq.XElement.Parse(unformattedXml).ToString();
                    }
                    catch
                    {
                        _cachedXml = unformattedXml;
                    }

                    _eventRecord = null;
                }

                return _cachedXml;
            }
        }
    }

    private string? _cachedXml = null;

    private EventRecord? _eventRecord;
}
