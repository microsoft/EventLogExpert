// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using System.Diagnostics.Eventing.Reader;

namespace EventLogExpert.Eventing.EventResolvers;

/// <summary>This IEventResolver uses event databases if any are available, and falls back to local providers if not.</summary>
public sealed class VersatileEventResolver : IEventResolver
{
    private readonly EventProviderDatabaseEventResolver? _databaseResolver;
    private readonly LocalProviderEventResolver? _localResolver;

    public VersatileEventResolver(IDatabaseCollectionProvider dbCollection, ITraceLogger tracer)
    {
        if (dbCollection.ActiveDatabases.IsEmpty)
        {
            _localResolver = new LocalProviderEventResolver(tracer.Trace);
        }
        else
        {
            _databaseResolver = new EventProviderDatabaseEventResolver(dbCollection, tracer.Trace);
        }

        tracer.Trace($"Database Resolver is {dbCollection.ActiveDatabases.IsEmpty} in {nameof(VersatileEventResolver)} constructor.");
    }

    public IEnumerable<string> GetKeywordsFromBitmask(EventRecord eventRecord)
    {
        if (_databaseResolver is not null)
        {
            return _databaseResolver.GetKeywordsFromBitmask(eventRecord);
        }

        if (_localResolver is not null)
        {
            return _localResolver.GetKeywordsFromBitmask(eventRecord);
        }

        throw new InvalidOperationException("No database or local resolver available.");
    }

    public string ResolveDescription(EventRecord eventRecord)
    {
        if (_databaseResolver is not null)
        {
            return _databaseResolver.ResolveDescription(eventRecord);
        }

        if (_localResolver is not null)
        {
            return _localResolver.ResolveDescription(eventRecord);
        }

        throw new InvalidOperationException("No database or local resolver available.");
    }

    public void ResolveProviderDetails(EventRecord eventRecord, string owningLogName)
    {
        if (_databaseResolver is not null)
        {
            _databaseResolver.ResolveProviderDetails(eventRecord, owningLogName);

            return;
        }

        if (_localResolver is not null)
        {
            _localResolver.ResolveProviderDetails(eventRecord, owningLogName);

            return;
        }

        throw new InvalidOperationException("No database or local resolver available.");
    }

    public string ResolveTaskName(EventRecord eventRecord)
    {
        if (_databaseResolver is not null)
        {
            return _databaseResolver.ResolveTaskName(eventRecord);
        }

        if (_localResolver is not null)
        {
            return _localResolver.ResolveTaskName(eventRecord);
        }

        throw new InvalidOperationException("No database or local resolver available.");
    }
}
