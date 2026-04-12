// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Providers;

namespace EventLogExpert.Eventing.EventResolvers;

/// <summary>
///     Resolves event descriptions by using our own logic to look up message strings in the providers available on
///     the local machine.
/// </summary>
internal sealed class LocalProviderEventResolver : EventResolverBase, IEventResolver
{
    internal LocalProviderEventResolver(IEventResolverCache? cache = null, ITraceLogger? logger = null)
        : base(cache, logger) { }

    public void ResolveProviderDetails(EventRecord eventRecord)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, nameof(LocalProviderEventResolver));

        // Fast path: ConcurrentDictionary is thread-safe for reads
        if (ProviderDetails.ContainsKey(eventRecord.ProviderName)) { return; }

        ProviderDetailsLock.EnterUpgradeableReadLock();

        try
        {
            // Re-check after acquiring lock
            if (ProviderDetails.ContainsKey(eventRecord.ProviderName)) { return; }

            ProviderDetailsLock.EnterWriteLock();

            try
            {
                if (ProviderDetails.ContainsKey(eventRecord.ProviderName)) { return; }

                var details = new EventMessageProvider(eventRecord.ProviderName, Logger).LoadProviderDetails();

                ProviderDetails.TryAdd(eventRecord.ProviderName, details);
            }
            finally
            {
                ProviderDetailsLock.ExitWriteLock();
            }
        }
        finally
        {
            ProviderDetailsLock.ExitUpgradeableReadLock();
        }
    }
}
