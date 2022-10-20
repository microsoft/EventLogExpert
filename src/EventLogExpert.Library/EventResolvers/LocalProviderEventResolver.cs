using EventLogExpert.Library.Helpers;
using EventLogExpert.Library.Models;
using EventLogExpert.Library.Providers;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;

namespace EventLogExpert.Library.EventResolvers;

/// <summary>
///     Resolves event descriptions by using our own logic to look up
///     message strings in the providers available on the local machine.
/// </summary>
public class LocalProviderEventResolver : IEventResolver
{
    private Dictionary<string, EventMessageProvider> _messageProviders = new();
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
                (SeverityLevel?)eventRecord.Level,
                eventRecord.ProviderName,
                "",
                "Description not found. No provider available.");
        }

        // TODO: Implement this
        return null;
    }
}
