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
public class LocalProviderEventResolver(Action<string, LogLevel> tracer) : EventResolverBase(tracer), IEventResolver
{
    public string Status { get; private set; } = string.Empty;

    public event EventHandler<string>? StatusChanged;

    private readonly ConcurrentDictionary<string, ProviderDetails?> _providerDetails = new();

    private bool _disposedValue;

    public LocalProviderEventResolver() : this((s, log) => Debug.WriteLine(s)) { }

    public DisplayEventModel Resolve(EventRecord eventRecord, string owningLogName)
    {
        if (!_providerDetails.TryGetValue(eventRecord.ProviderName, out var providerDetails))
        {
            providerDetails = new EventMessageProvider(eventRecord.ProviderName, _tracer).LoadProviderDetails();
            _providerDetails.TryAdd(eventRecord.ProviderName, providerDetails);
        }

        if (providerDetails is null)
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
                eventRecord.Qualifiers,
                GetKeywordsFromBitmask(eventRecord.Keywords, null),
                eventRecord.ProcessId,
                eventRecord.ThreadId,
                eventRecord.UserId,
                eventRecord.LogName,
                owningLogName,
                eventRecord.ToXml());
        }

        return ResolveFromProviderDetails(eventRecord, eventRecord.Properties, providerDetails, owningLogName);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            _providerDetails.Clear();

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
