// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Providers;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace EventLogExpert.Eventing.EventResolvers;

/// <summary>
///     Resolves event descriptions by using our own logic to look up
///     message strings in the providers available on the local machine.
/// </summary>
public sealed class LocalProviderEventResolver(Action<string, LogLevel> tracer) : EventResolverBase(tracer), IEventResolver
{
    public LocalProviderEventResolver() : this((s, log) => Debug.WriteLine(s)) { }

    public string GetXml(EventRecord eventRecord) { return string.Empty; }

    public void ResolveProviderDetails(EventRecord eventRecord, string owningLogName)
    {
        providerDetailsLock.EnterUpgradeableReadLock();

        try
        {
            if (providerDetails.ContainsKey(eventRecord.ProviderName))
            {
                return;
            }

            providerDetailsLock.EnterWriteLock();

            try
            {
                var details = new EventMessageProvider(eventRecord.ProviderName, tracer).LoadProviderDetails();
                providerDetails.TryAdd(eventRecord.ProviderName, details);
            }
            finally
            {
                providerDetailsLock.ExitWriteLock();
            }
        }
        finally
        {
            providerDetailsLock.ExitUpgradeableReadLock();
        }
    }
}
