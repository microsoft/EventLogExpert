// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using System.Diagnostics.Eventing.Reader;

namespace EventLogExpert.Eventing.EventResolvers;

/// <summary>This IEventResolver uses event databases if any are available, and falls back to local providers if not.</summary>
public class VersatileEventResolver : IEventResolver
{
    private readonly EventProviderDatabaseEventResolver _databaseResolver;
    private readonly LocalProviderEventResolver _localResolver;
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

    public IEnumerable<string> GetKeywordsFromBitmask(EventRecord eventRecord) =>
        _useDatabaseResolver
            ? _databaseResolver.GetKeywordsFromBitmask(eventRecord)
            : _localResolver.GetKeywordsFromBitmask(eventRecord);

    public string ResolveDescription(EventRecord eventRecord) =>
        _useDatabaseResolver ?
            _databaseResolver.ResolveDescription(eventRecord) :
            _localResolver.ResolveDescription(eventRecord);

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

    public string ResolveTaskName(EventRecord eventRecord) =>
        _useDatabaseResolver ?
            _databaseResolver.ResolveTaskName(eventRecord) :
            _localResolver.ResolveTaskName(eventRecord);

    protected virtual void Dispose(bool disposing)
    {
        if (_disposedValue) { return; }

        if (disposing)
        {
            _databaseResolver.Dispose();
            _localResolver.Dispose();
        }

        _disposedValue = true;
    }
}
