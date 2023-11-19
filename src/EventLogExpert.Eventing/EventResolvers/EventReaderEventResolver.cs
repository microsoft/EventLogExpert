// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using System.Diagnostics.Eventing.Reader;

namespace EventLogExpert.Eventing.EventResolvers;

/// <summary>
///     Resolves event descriptions using the FormatDescription method
///     built into the EventRecord objects returned by EventLogReader.
///     Note the EventLogReader must not be disposed yet for this to work.
/// </summary>
public class EventReaderEventResolver : IEventResolver
{
    private bool _disposedValue;

    public string Status { get; private set; } = string.Empty;

    public DisplayEventModel Resolve(EventRecord eventRecord, string owningLogName)
    {
        var desc = eventRecord.FormatDescription();
        
        IEnumerable<string> keywordsDisplayNames;
        try
        {
            keywordsDisplayNames = eventRecord.KeywordsDisplayNames;
        }
        catch
        {
            keywordsDisplayNames = [];
        }

        return new DisplayEventModel(
            eventRecord.RecordId,
            eventRecord.ActivityId,
            eventRecord.TimeCreated!.Value.ToUniversalTime(),
            eventRecord.Id,
            eventRecord.MachineName,
            Severity.GetString(eventRecord.Level),
            eventRecord.ProviderName,
            eventRecord.Task is 0 or null ? "None" : TryGetValue(() => eventRecord.TaskDisplayName),
            string.IsNullOrEmpty(desc) ? string.Empty : desc,
            eventRecord.Properties,
            eventRecord.Qualifiers,
            eventRecord.Keywords,
            keywordsDisplayNames,
            eventRecord.ProcessId,
            eventRecord.ThreadId,
            eventRecord.LogName,
            null,
            owningLogName);
    }

    private static T TryGetValue<T>(Func<T> func)
    {
        try
        {
            var result = func();
            return result;
        }
        catch
        {
            return default!;
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
