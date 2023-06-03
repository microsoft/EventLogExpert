// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.EventProviderDatabase;
using EventLogExpert.Library.Helpers;
using EventLogExpert.Library.Models;
using EventLogExpert.Library.Providers;
using System.Collections.Concurrent;
using System.Diagnostics.Eventing.Reader;
using System.Text.RegularExpressions;

namespace EventLogExpert.Library.EventResolvers;

public class EventProviderDatabaseEventResolver : EventResolverBase, IEventResolver, IDisposable
{
    public string Status { get; private set; } = string.Empty;

    public event EventHandler<string>? StatusChanged;

    private readonly ConcurrentBag<EventProviderDbContext> dbContexts = new();

    private readonly ConcurrentDictionary<string, ProviderDetails?> _providerDetails = new();

    private readonly SemaphoreSlim _databaseAccessSemaphore = new SemaphoreSlim(1);

    private bool disposedValue;

    public EventProviderDatabaseEventResolver(IDatabaseCollectionProvider dbCollection) : this(dbCollection, s => { }) { }

    public EventProviderDatabaseEventResolver(IDatabaseCollectionProvider dbCollection, Action<string> tracer) : base(tracer)
    {
        tracer($"{nameof(EventProviderDatabaseEventResolver)} was instantiated at:\n{Environment.StackTrace}");
        LoadDatabases(dbCollection.ActiveDatabases);
    }

    /// <summary>
    /// Loads the databases. If ActiveDatabases is populated, any databases
    /// not named therein are skipped.
    /// </summary>
    private void LoadDatabases(IEnumerable<string> databasePaths)
    {
        _tracer($"{nameof(LoadDatabases)} was called with {databasePaths.Count()} {nameof(databasePaths)}.");
        foreach (var databasePath in databasePaths)
        {
            _tracer($"  {databasePath}");
        }

        _providerDetails.Clear();

        foreach (var context in dbContexts)
        {
            context.Dispose();
        }

        dbContexts.Clear();

        var databasesToLoad = SortDatabases(databasePaths);

        var obsoleteDbs = new List<string>();
        foreach (var file in databasesToLoad)
        {
            if (!File.Exists(file))
            {
                throw new FileNotFoundException(file);
            }

            var c = new EventProviderDbContext(file, readOnly: true, _tracer);
            var (needsv2, needsv3) = c.IsUpgradeNeeded();
            if (needsv2 || needsv3)
            {
                obsoleteDbs.Add(file);
                c.Dispose();
                continue;
            }

            c.ChangeTracker.QueryTrackingBehavior = Microsoft.EntityFrameworkCore.QueryTrackingBehavior.NoTracking;
            dbContexts.Add(c);
        }

        if (obsoleteDbs.Any())
        {
            foreach (var db in dbContexts)
            {
                db.Dispose();
            }

            dbContexts.Clear();

            throw new InvalidOperationException("Obsolete DB format: " + string.Join(' ', obsoleteDbs.Select(db => Path.GetFileName(db))));
        }
    }

    /// <summary>
    /// If the database file name ends in a year or a number, such as Exchange 2019 or
    /// Windows 2016, we want to sort the database files by descending version, but by
    /// ascending product name. This generally means that databases named for products
    /// like Exchange will be checked for matching providers first, and Windows will
    /// be checked last, with newer versions being checked before older versions.
    /// </summary>
    /// <param name="databasePaths"></param>
    /// <returns></returns>
    private static IEnumerable<string> SortDatabases(IEnumerable<string> databasePaths)
    {
        if (!databasePaths.Any())
        {
            return Array.Empty<string>();
        }

        var r = new Regex("^(.+) (\\S+)$");

        return databasePaths
            .Select(path =>
            {
                var name = Path.GetFileName(path);
                var directory = Path.GetDirectoryName(path);
                var m = r.Match(name);
                if (m.Success)
                {
                    return new
                    {
                        Directory = directory,
                        FirstPart = m.Groups[1].Value + " ",
                        SecondPart = m.Groups[2].Value
                    };
                }
                else
                {
                    return new
                    {
                        Directory = directory,
                        FirstPart = name,
                        SecondPart = ""
                    };
                }
            })
            .OrderBy(n => n.FirstPart)
            .ThenByDescending(n => n.SecondPart)
            .Select(n => Path.Join(n.Directory, n.FirstPart + n.SecondPart))
            .ToList();
    }

    public DisplayEventModel Resolve(EventRecord eventRecord, string OwningLogName)
    {
        DisplayEventModel lastResult = null!;

        // The Properties getter is expensive, so we only call the getter once,
        // and we pass this value separately from the eventRecord so it can be reused.
        var eventProperties = eventRecord.Properties;

        if (_providerDetails.ContainsKey(eventRecord.ProviderName))
        {
            _providerDetails.TryGetValue(eventRecord.ProviderName, out ProviderDetails? providerDetails);
            if (providerDetails != null)
            {
                lastResult = ResolveFromProviderDetails(eventRecord, eventProperties, providerDetails, OwningLogName);
            }
        }
        else
        {
            _databaseAccessSemaphore.Wait();

            try
            {
                foreach (var dbContext in dbContexts)
                {
                    var providerDetails = dbContext.ProviderDetails.FirstOrDefault(p => p.ProviderName.ToLower() == eventRecord.ProviderName.ToLower());
                    if (providerDetails != null)
                    {
                        lastResult = ResolveFromProviderDetails(eventRecord, eventProperties, providerDetails, OwningLogName);

                        if (lastResult?.Description != null)
                        {
                            _tracer($"Resolved {eventRecord.ProviderName} provider from database {dbContext.Name}.");
                            _providerDetails.TryAdd(eventRecord.ProviderName, providerDetails);
                            return lastResult;
                        }
                    }
                }
            }
            finally
            {
                _databaseAccessSemaphore.Release();
            }

            if (!_providerDetails.ContainsKey(eventRecord.ProviderName))
            {
                _providerDetails.TryAdd(eventRecord.ProviderName, new ProviderDetails { ProviderName = eventRecord.ProviderName });
            }
        }

        if (lastResult == null)
        {
            return new DisplayEventModel(
                eventRecord.RecordId,
                eventRecord.TimeCreated!.Value.ToUniversalTime(),
                eventRecord.Id,
                eventRecord.MachineName,
                (SeverityLevel?)eventRecord.Level,
                eventRecord.ProviderName,
                "",
                "Description not found. No provider available.",
                eventProperties,
                eventRecord.Qualifiers,
                eventRecord.Keywords,
                GetKeywordsFromBitmask(eventRecord.Keywords, null),
                eventRecord.LogName,
                null,
                OwningLogName);
        }

        if (lastResult.Description == null)
        {
            lastResult = lastResult with { Description = "" };
        }

        return lastResult;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                foreach (var context in dbContexts)
                {
                    context.Dispose();
                }
            }

            _providerDetails.Clear();

            disposedValue = true;

            _tracer($"{nameof(EventProviderDatabaseEventResolver)} Disposed at:\n{Environment.StackTrace}");
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
