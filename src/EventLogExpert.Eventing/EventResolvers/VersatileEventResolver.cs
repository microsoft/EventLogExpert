// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;

namespace EventLogExpert.Eventing.EventResolvers;

/// <summary>This IEventResolver uses event databases if any are available, and falls back to local providers if not.</summary>
public sealed class VersatileEventResolver : IEventResolver
{
    private readonly EventProviderDatabaseEventResolver? _databaseResolver;
    private readonly LocalProviderEventResolver? _localResolver;

    public VersatileEventResolver(
        IDatabaseCollectionProvider? dbCollection = null,
        IEventResolverCache? cache = null,
        ITraceLogger? tracer = null)
    {
        if (dbCollection is null || dbCollection.ActiveDatabases.IsEmpty)
        {
            _localResolver = new LocalProviderEventResolver(cache, tracer);
        }
        else
        {
            _databaseResolver = new EventProviderDatabaseEventResolver(dbCollection, cache, tracer);
        }

        tracer?.Trace(
            $"Database Resolver is {dbCollection?.ActiveDatabases.IsEmpty} in {nameof(VersatileEventResolver)} constructor.");
    }

    public DisplayEventModel ResolveEvent(EventRecord eventRecord)
    {
        ResolveProviderDetails(eventRecord);

        if (_databaseResolver is not null)
        {
            return _databaseResolver.ResolveEvent(eventRecord);
        }

        if (_localResolver is not null)
        {
            return _localResolver.ResolveEvent(eventRecord);
        }

        throw new InvalidOperationException("No database or local resolver available.");
    }

    public void ResolveProviderDetails(EventRecord eventRecord)
    {
        if (_databaseResolver is not null)
        {
            _databaseResolver.ResolveProviderDetails(eventRecord);

            return;
        }

        if (_localResolver is not null)
        {
            _localResolver.ResolveProviderDetails(eventRecord);

            return;
        }

        throw new InvalidOperationException("No database or local resolver available.");
    }
}
