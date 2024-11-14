// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Providers;

namespace EventLogExpert.Eventing.EventResolvers;

/// <summary>
///     Resolves event descriptions by using our own logic to look up
///     message strings in the providers available on the local machine.
/// </summary>
public sealed class LocalProviderEventResolver(ITraceLogger? logger = null) : EventResolverBase(logger), IEventResolver
{
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
                // Double-check in case another thread added the provider details while we were waiting.
                if (providerDetails.ContainsKey(eventRecord.ProviderName))
                {
                    return;
                }

                var details = new EventMessageProvider(eventRecord.ProviderName, logger).LoadProviderDetails();

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
