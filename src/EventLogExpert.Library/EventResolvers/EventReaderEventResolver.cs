using EventLogExpert.Library.Models;
using System.Diagnostics.Eventing.Reader;

namespace EventLogExpert.Library.EventResolvers
{
    /// <summary>
    /// Resolves event descriptions using the FormatDescription method
    /// built into the EventRecord objects returned by EventLogReader.
    /// Note the EventLogReader must not be disposed yet for this to work.
    /// </summary>
    public class EventReaderEventResolver : IEventResolver
    {
        private static readonly Dictionary<byte, string> LevelNames = new()
        {
            { 0, "Information" },
            { 2, "Error" },
            { 3, "Warning" },
            { 4, "Information" }
        };

        public DisplayEventModel Resolve(EventRecord eventRecord)
        {
            return new DisplayEventModel(
                    eventRecord.RecordId,
                    eventRecord.TimeCreated,
                    eventRecord.Id,
                    eventRecord.MachineName,
                    LevelNames[eventRecord.Level ?? 0],
                    eventRecord.ProviderName,
                    eventRecord.Task is 0 or null ? "None" : TryGetValue(() => eventRecord.TaskDisplayName),
                    eventRecord.FormatDescription());
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
}
