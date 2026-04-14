// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventProviderDatabase;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Providers;
using Microsoft.EntityFrameworkCore;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace EventLogExpert.Eventing.EventResolvers;

/// <summary>
/// Event resolver that uses SQLite databases to resolve event provider details.
/// This class implements IDisposable and manages EF DbContext instances.
/// IMPORTANT: Callers must dispose this resolver to ensure database files are not left locked.
/// </summary>
internal sealed partial class EventProviderDatabaseEventResolver : EventResolverBase, IEventResolver
{
    private readonly Lock _databaseAccessLock = new();

    private ImmutableArray<EventProviderDbContext> _dbContexts = [];

    internal EventProviderDatabaseEventResolver(
        IDatabaseCollectionProvider dbCollection,
        IEventResolverCache? cache = null,
        ITraceLogger? logger = null) : base(cache, logger)
    {
        ArgumentNullException.ThrowIfNull(dbCollection);

        Logger?.Trace($"{nameof(EventProviderDatabaseEventResolver)} was instantiated at:\n{Environment.StackTrace}");

        LoadDatabases(dbCollection.ActiveDatabases);
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing) { return; }

        ImmutableArray<EventProviderDbContext> contextsToDispose;

        // Use EnterScope() for exception-safe lock management.
        // This ensures the lock is always released even if an exception occurs.
        using (_databaseAccessLock.EnterScope())
        {
            // Check if already disposed
            if (IsDisposed)
            {
                return;
            }

            // Swap out _dbContexts to ensure exactly-once context disposal.
            // Only one thread will get the non-empty array; others get empty.
            // The lock synchronizes with ResolveProviderDetails to prevent use-after-dispose.
            contextsToDispose = _dbContexts;
            _dbContexts = [];
        }

        // Dispose contexts outside the lock to minimize lock hold time.
        // Safe because we have the only reference to these contexts now.
        foreach (var context in contextsToDispose)
        {
            context.Dispose();
        }

        // Call base to dispose providerDetailsLock and set IsDisposed flag.
        base.Dispose(disposing);
    }

    public void ResolveProviderDetails(EventRecord eventRecord)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, nameof(EventProviderDatabaseEventResolver));

        // Fast path: ConcurrentDictionary is thread-safe for reads, so we can check
        // without any lock. This avoids serializing all parallel reader threads for
        // providers that are already cached.
        if (ProviderDetails.ContainsKey(eventRecord.ProviderName)) { return; }

        using (ProviderDetailsLock.EnterScope())
        {
            // Re-check after acquiring lock - another thread may have added this provider
            if (ProviderDetails.ContainsKey(eventRecord.ProviderName)) { return; }

            // Use EnterScope() for exception-safe lock management
            using (_databaseAccessLock.EnterScope())
            {
                // Check disposed inside the database lock to prevent race with Dispose().
                // This ensures we don't proceed if Dispose() has swapped out _dbContexts.
                ObjectDisposedException.ThrowIf(IsDisposed, nameof(EventProviderDatabaseEventResolver));

                foreach (var dbContext in _dbContexts)
                {
                    // Use EF.Functions.Collate() with NOCASE on both sides for case-insensitive comparison.
                    // Applying to both sides ensures the comparison is case-insensitive regardless of the
                    // case in eventRecord.ProviderName. SQLite will use NOCASE collation for the comparison.
                    // Note: The loop over databases is intentional for priority-based resolution.
                    var details = dbContext.ProviderDetails.FirstOrDefault(p =>
                        EF.Functions.Collate(p.ProviderName, "NOCASE") ==
                        EF.Functions.Collate(eventRecord.ProviderName, "NOCASE"));

                    if (details is null) { continue; }

                    Logger?.Debug($"Resolved {eventRecord.ProviderName} provider from database {dbContext.Name}.");
                    ProviderDetails.TryAdd(eventRecord.ProviderName, details);

                    // Exit after first match - databases are sorted by priority (SortDatabases),
                    // so the first database containing the provider is the preferred source.
                    // TryAdd would prevent duplicates anyway, but breaking early avoids unnecessary queries.
                    break;
                }
            }

            if (!ProviderDetails.ContainsKey(eventRecord.ProviderName))
            {
                ProviderDetails.TryAdd(eventRecord.ProviderName, new ProviderDetails { ProviderName = eventRecord.ProviderName });
            }
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
    internal static IEnumerable<string> SortDatabases(IEnumerable<string> databasePaths)
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
                    var versionString = m.Groups[2].Value;

                    // Strip file extension if present for numeric parsing
                    var versionWithoutExtension = Path.GetFileNameWithoutExtension(versionString);

                    // Try to parse the version as a number for proper numeric ordering.
                    // This ensures "10" sorts after "2" rather than before it (lexicographic).
                    int? numericVersion = int.TryParse(versionWithoutExtension, out var parsed) ? parsed : null;

                    return new
                    {
                        Directory = directory,
                        FirstPart = m.Groups[1].Value + " ",
                        SecondPart = versionString,
                        NumericVersion = numericVersion
                    };
                }

                return new
                {
                    Directory = directory,
                    FirstPart = name,
                    SecondPart = "",
                    NumericVersion = (int?)null
                };
            })
            .OrderBy(n => n.FirstPart)
            .ThenByDescending(n => n.NumericVersion ?? int.MinValue)
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
        Logger?.Debug($"{nameof(LoadDatabases)} was called with {databasePaths.Count()} {nameof(databasePaths)}.");

        foreach (var databasePath in databasePaths)
        {
            Logger?.Debug($"  {databasePath}");
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

            var c = new EventProviderDbContext(file, readOnly: true, Logger);
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
