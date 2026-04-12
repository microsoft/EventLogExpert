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
        ProviderDetailsLock.EnterUpgradeableReadLock();

        try
        {
            ObjectDisposedException.ThrowIf(IsDisposed, nameof(LocalProviderEventResolver));

            if (ProviderDetails.ContainsKey(eventRecord.ProviderName)) { return; }

            ProviderDetailsLock.EnterWriteLock();

            try
            {
                // Double-check in case another thread added the provider details while we were waiting.
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
