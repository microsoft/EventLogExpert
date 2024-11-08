// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventProviderDatabase;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Providers;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace EventLogExpert.Eventing.EventResolvers;

public sealed partial class EventProviderDatabaseEventResolver : EventResolverBase, IEventResolver
{
    private readonly SemaphoreSlim _databaseAccessSemaphore = new(1);

    private ImmutableArray<EventProviderDbContext> _dbContexts = [];

    public EventProviderDatabaseEventResolver(IDatabaseCollectionProvider dbCollection) : this(dbCollection, (s, log) => { }) { }

    public EventProviderDatabaseEventResolver(IDatabaseCollectionProvider dbCollection, Action<string, LogLevel> tracer) : base(tracer)
    {
        tracer($"{nameof(EventProviderDatabaseEventResolver)} was instantiated at:\n{Environment.StackTrace}", LogLevel.Information);
        LoadDatabases(dbCollection.ActiveDatabases);
    }

    public void ResolveProviderDetails(EventRecord eventRecord, string owningLogName)
    {
        providerDetailsLock.EnterUpgradeableReadLock();

        try
        {
            if (providerDetails.ContainsKey(eventRecord.ProviderName))
            {
                return;
            }

            providerDetailsLock.EnterWriteLock();

            try
            {
                _databaseAccessSemaphore.Wait();

                foreach (var dbContext in _dbContexts)
                {
                    var details = dbContext.ProviderDetails.FirstOrDefault(p => p.ProviderName.ToLower() == eventRecord.ProviderName.ToLower());

                    if (details is null) { continue; }

                    tracer($"Resolved {eventRecord.ProviderName} provider from database {dbContext.Name}.", LogLevel.Information);
                    providerDetails.TryAdd(eventRecord.ProviderName, details);
                }
            }
            finally
            {
                _databaseAccessSemaphore.Release();
                providerDetailsLock.ExitWriteLock();
            }

            if (!providerDetails.ContainsKey(eventRecord.ProviderName))
            {
                providerDetails.TryAdd(eventRecord.ProviderName, new ProviderDetails { ProviderName = eventRecord.ProviderName });
            }
        }
        finally
        {
            providerDetailsLock.ExitUpgradeableReadLock();
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
            return [];
        }

        var r = SplitProductAndVersionRegex();

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

                return new
                {
                    Directory = directory,
                    FirstPart = name,
                    SecondPart = ""
                };
            })
            .OrderBy(n => n.FirstPart)
            .ThenByDescending(n => n.SecondPart)
            .Select(n => Path.Join(n.Directory, n.FirstPart + n.SecondPart));
    }

    [GeneratedRegex("^(.+) (\\S+)$")]
    private static partial Regex SplitProductAndVersionRegex();

    /// <summary>
    /// Loads the databases. If ActiveDatabases is populated, any databases
    /// not named therein are skipped.
    /// </summary>
    private void LoadDatabases(IEnumerable<string> databasePaths)
    {
        tracer($"{nameof(LoadDatabases)} was called with {databasePaths.Count()} {nameof(databasePaths)}.", LogLevel.Information);

        foreach (var databasePath in databasePaths)
        {
            tracer($"  {databasePath}", LogLevel.Information);
        }

        foreach (var context in _dbContexts)
        {
            context.Dispose();
        }

        _dbContexts = [];

        var databasesToLoad = SortDatabases(databasePaths);
        var obsoleteDbs = new List<string>();
        var newContexts = new List<EventProviderDbContext>();

        foreach (var file in databasesToLoad)
        {
            if (!File.Exists(file))
            {
                throw new FileNotFoundException(file);
            }

            var c = new EventProviderDbContext(file, readOnly: true, tracer);
            var (needsv2, needsv3) = c.IsUpgradeNeeded();
            if (needsv2 || needsv3)
            {
                obsoleteDbs.Add(file);
                c.Dispose();
                continue;
            }

            c.ChangeTracker.QueryTrackingBehavior = Microsoft.EntityFrameworkCore.QueryTrackingBehavior.NoTracking;
            newContexts.Add(c);
        }

        _dbContexts = [.. newContexts];

        if (obsoleteDbs.Count > 0)
        {
            foreach (var db in _dbContexts)
            {
                db.Dispose();
            }

            _dbContexts = [];

            throw new InvalidOperationException("Obsolete DB format: " + string.Join(' ', obsoleteDbs.Select(Path.GetFileName)));
        }
    }
}
