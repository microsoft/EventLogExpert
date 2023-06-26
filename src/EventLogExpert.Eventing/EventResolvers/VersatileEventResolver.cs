// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using Microsoft.Extensions.Logging;
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
    private readonly ITraceLogger _tracer;

    private volatile bool _useDatabaseResolver = false;
    private bool disposedValue = false;

    public string Status => _useDatabaseResolver ? _databaseResolver.Status : _localResolver.Status;

    public event EventHandler<string>? StatusChanged;

    public VersatileEventResolver(IDatabaseCollectionProvider dbCollection, ITraceLogger tracer)
    {
        _localResolver = new LocalProviderEventResolver(tracer.Trace);
        _databaseResolver = new EventProviderDatabaseEventResolver(dbCollection, tracer.Trace);
        _useDatabaseResolver = dbCollection.ActiveDatabases.Any();
        _tracer = tracer;
        _tracer.Trace($"{nameof(_useDatabaseResolver)} is {_useDatabaseResolver} in {nameof(VersatileEventResolver)} constructor.");
        _databaseResolver.StatusChanged += (sender, args) => StatusChanged?.Invoke(sender, args);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public DisplayEventModel Resolve(EventRecord eventRecord, string OwningLogName)
    {
        return _useDatabaseResolver ? _databaseResolver.Resolve(eventRecord, OwningLogName) : _localResolver.Resolve(eventRecord, OwningLogName);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                _databaseResolver.Dispose();
            }

            disposedValue = true;
        }
    }
}
