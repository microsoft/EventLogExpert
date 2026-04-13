// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;

namespace EventLogExpert.Eventing.EventResolvers;

/// <summary>
/// This IEventResolver uses event databases if any are available, and falls back to local providers if not.
/// IMPORTANT: This class implements IDisposable because it may hold database resources.
/// Callers must dispose this resolver to ensure database files are not left locked.
/// </summary>
public sealed class VersatileEventResolver : IEventResolver
{
    private readonly EventProviderDatabaseEventResolver? _databaseResolver;
    private readonly LocalProviderEventResolver? _localResolver;

    private volatile bool _disposed;

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

        tracer?.Debug($"Database Resolver is {dbCollection?.ActiveDatabases.IsEmpty} in {nameof(VersatileEventResolver)} constructor.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _databaseResolver?.Dispose();
        _localResolver?.Dispose();
    }

    public DisplayEventModel ResolveEvent(EventRecord eventRecord)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(VersatileEventResolver));

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
        ObjectDisposedException.ThrowIf(_disposed, nameof(VersatileEventResolver));

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
