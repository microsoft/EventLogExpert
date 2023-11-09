// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Providers;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;

namespace EventLogExpert.Eventing.EventResolvers;

/// <summary>
///     Resolves event descriptions by using our own logic to look up
///     message strings in the providers available on the local machine.
/// </summary>
public class LocalProviderEventResolver : EventResolverBase, IEventResolver
{
    public string Status { get; private set; } = string.Empty;

    public event EventHandler<string>? StatusChanged;

    private readonly ConcurrentDictionary<string, ProviderDetails?> _providerDetails = new();

    private bool disposedValue;

    public LocalProviderEventResolver() : base((s, log) => Debug.WriteLine(s)) { }

    public LocalProviderEventResolver(Action<string, LogLevel> tracer) : base(tracer) { }

    public DisplayEventModel Resolve(EventRecord eventRecord, string OwningLogName)
    {
        if (!_providerDetails.ContainsKey(eventRecord.ProviderName))
        {
            var provider = new EventMessageProvider(eventRecord.ProviderName, _tracer);
            _providerDetails.TryAdd(eventRecord.ProviderName, provider.LoadProviderDetails());
        }

        _providerDetails.TryGetValue(eventRecord.ProviderName, out var providerDetails);

        if (providerDetails == null)
        {
            return new DisplayEventModel(
                eventRecord.RecordId,
                eventRecord.ActivityId,
                eventRecord.TimeCreated!.Value.ToUniversalTime(),
                eventRecord.Id,
                eventRecord.MachineName,
                Severity.GetString(eventRecord.Level),
                eventRecord.ProviderName,
                "",
                "Description not found. No provider available.",
                eventRecord.Properties,
                eventRecord.Qualifiers,
                eventRecord.Keywords,
                GetKeywordsFromBitmask(eventRecord.Keywords, null),
                eventRecord.ProcessId,
                eventRecord.ThreadId,
                eventRecord.LogName,
                null,
                OwningLogName);
        }

        // The Properties getter is expensive, so we only call the getter once,
        // and we pass this value separately from the eventRecord so it can be reused.
        var eventProperties = eventRecord.Properties;

        var result = ResolveFromProviderDetails(eventRecord, eventProperties, providerDetails, OwningLogName);

        if (result.Description == null)
        {
            result = result with { Description = "" };
        }

        return result;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            _providerDetails.Clear();

            disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
