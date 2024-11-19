// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventProviderDatabase;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Providers;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace EventLogExpert.Eventing.EventResolvers;

internal sealed partial class EventProviderDatabaseEventResolver : EventResolverBase, IEventResolver
{
    private readonly Lock _databaseAccessLock = new();

    private ImmutableArray<EventProviderDbContext> _dbContexts = [];

    internal EventProviderDatabaseEventResolver(
        IDatabaseCollectionProvider dbCollection,
        IEventResolverCache? cache = null,
        ITraceLogger? logger = null) : base(cache, logger)
    {
        logger?.Trace($"{nameof(EventProviderDatabaseEventResolver)} was instantiated at:\n{Environment.StackTrace}");

        LoadDatabases(dbCollection.ActiveDatabases);
    }

    public void ResolveProviderDetails(EventRecord eventRecord)
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
                _databaseAccessLock.Enter();

                foreach (var dbContext in _dbContexts)
                {
                    var details = dbContext.ProviderDetails.FirstOrDefault(p => p.ProviderName.ToLower() == eventRecord.ProviderName.ToLower());

                    if (details is null) { continue; }

                    logger?.Trace($"Resolved {eventRecord.ProviderName} provider from database {dbContext.Name}.");
                    providerDetails.TryAdd(eventRecord.ProviderName, details);
                }
            }
            finally
            {
                _databaseAccessLock.Exit();
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
        logger?.Trace($"{nameof(LoadDatabases)} was called with {databasePaths.Count()} {nameof(databasePaths)}.");

        foreach (var databasePath in databasePaths)
        {
            logger?.Trace($"  {databasePath}");
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

            var c = new EventProviderDbContext(file, readOnly: true, logger);
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
