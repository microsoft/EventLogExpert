// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using System.Diagnostics.Eventing.Reader;

namespace EventLogExpert.Eventing.EventResolvers;

/// <summary>
/// This IEventResolver uses event databases if any are
/// available, and falls back to local providers if not.
/// </summary>
public class VersatileEventResolver : IEventResolver, IDisposable
{
    private readonly LocalProviderEventResolver _localResolver;
    private readonly EventProviderDatabaseEventResolver _databaseResolver;
    private readonly bool _useDatabaseResolver;

    private bool _disposedValue;

    public VersatileEventResolver(IDatabaseCollectionProvider dbCollection, ITraceLogger tracer)
    {
        _localResolver = new LocalProviderEventResolver(tracer.Trace);
        _databaseResolver = new EventProviderDatabaseEventResolver(dbCollection, tracer.Trace);
        _useDatabaseResolver = !dbCollection.ActiveDatabases.IsEmpty;
        tracer.Trace($"{nameof(_useDatabaseResolver)} is {_useDatabaseResolver} in {nameof(VersatileEventResolver)} constructor.");
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public void ResolveProviderDetails(EventRecord eventRecord, string owningLogName)
    {
        if (_useDatabaseResolver)
        {
            _databaseResolver.ResolveProviderDetails(eventRecord, owningLogName);
        }
        else
        {
            _localResolver.ResolveProviderDetails(eventRecord, owningLogName);
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _databaseResolver.Dispose();
            }

            _disposedValue = true;
        }
    }
}
