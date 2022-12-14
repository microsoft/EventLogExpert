// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Helpers;
using EventLogExpert.Library.Models;
using System.Diagnostics.Eventing.Reader;

namespace EventLogExpert.Library.EventResolvers;

/// <summary>
///     Resolves event descriptions using the FormatDescription method
///     built into the EventRecord objects returned by EventLogReader.
///     Note the EventLogReader must not be disposed yet for this to work.
/// </summary>
public class EventReaderEventResolver : IEventResolver
{
    public DisplayEventModel Resolve(EventRecord eventRecord)
    {
        return new DisplayEventModel(
            eventRecord.RecordId,
            eventRecord.TimeCreated,
            eventRecord.Id,
            eventRecord.MachineName,
            (SeverityLevel?)eventRecord.Level,
            eventRecord.ProviderName,
            eventRecord.Task is 0 or null ? "None" : TryGetValue(() => eventRecord.TaskDisplayName),
            string.IsNullOrEmpty(eventRecord.FormatDescription()) ? string.Empty : eventRecord.FormatDescription());
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
            return default;
        }
    }
}
