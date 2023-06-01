// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using System.Collections.Immutable;
using System.Diagnostics.Eventing.Reader;

namespace EventLogExpert.Library.EventResolvers;

/// <summary>
/// This IEventResolver uses event databases if any are
/// available, and falls back to local providers if not.
/// </summary>
public class VersatileEventResolver : IDatabaseEventResolver, IEventResolver, IDisposable
{
    private readonly LocalProviderEventResolver _localResolver;
    private readonly EventProviderDatabaseEventResolver _databaseResolver;
    private readonly Action<string> _tracer;

    private volatile bool _useDatabaseResolver = false;
    private bool disposedValue = false;

    public string Status => _useDatabaseResolver ? _databaseResolver.Status : _localResolver.Status;

    public ImmutableArray<string> ActiveDatabases => _databaseResolver.ActiveDatabases;

    public event EventHandler<string>? StatusChanged;

    public VersatileEventResolver(LocalProviderEventResolver localResolver, EventProviderDatabaseEventResolver databaseResolver, Action<string> tracer)
    {
        _localResolver = localResolver;
        _databaseResolver = databaseResolver;
        _useDatabaseResolver = databaseResolver.ActiveDatabases.Any();
        _tracer = tracer;
        _tracer($"{nameof(_useDatabaseResolver)} is {_useDatabaseResolver} in {nameof(VersatileEventResolver)} constructor.");
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

    public void SetActiveDatabases(IEnumerable<string> databasePaths)
    {
        _useDatabaseResolver = databasePaths.Any();
        _tracer($"{nameof(_useDatabaseResolver)} is {_useDatabaseResolver} after call to {nameof(SetActiveDatabases)} in {nameof(VersatileEventResolver)}.");
        _databaseResolver.SetActiveDatabases(databasePaths);
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
