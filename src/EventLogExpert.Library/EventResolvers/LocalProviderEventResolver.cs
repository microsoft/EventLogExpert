using EventLogExpert.Library.Models;
using EventLogExpert.Library.Providers;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;

namespace EventLogExpert.Library.EventResolvers
{
    /// <summary>
    /// Resolves event descriptions by using our own logic to look up
    /// message strings in the providers available on the local machine.
    /// </summary>
    public class LocalProviderEventResolver : IEventResolver
    {
        private static readonly Dictionary<byte, string> LevelNames = new()
        {
            { 0, "Information" },
            { 2, "Error" },
            { 3, "Warning" },
            { 4, "Information" }
        };

        private Dictionary<string, EventMessageProvider> _messageProviders = new Dictionary<string, EventMessageProvider>();

        private Action<string> _tracer;

        public LocalProviderEventResolver()
        {
            _tracer = s => Debug.WriteLine(s);
        }

        public LocalProviderEventResolver(Action<string> tracer)
        {
            _tracer = tracer ?? throw new ArgumentNullException(nameof(tracer));
        }

        public DisplayEventModel Resolve(EventRecord eventRecord)
        {
            _messageProviders.TryGetValue(eventRecord.ProviderName, out EventMessageProvider provider);
            if (provider == null)
            {
                provider = new EventMessageProvider(eventRecord.ProviderName, _tracer);
                provider.LoadProviderDetails();
                _messageProviders.Add(eventRecord.ProviderName, provider);
            }

            if (provider == null)
            {
                return new DisplayEventModel(
                    eventRecord.RecordId,
                    eventRecord.TimeCreated,
                    eventRecord.Id,
                    eventRecord.MachineName,
                    LevelNames[eventRecord.Level ?? 0],
                    eventRecord.ProviderName,
                    "",
                    "Description not found. No provider available.");
            }

            // TODO: Implement this
            return null;
        }
    }
}
