// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventProviderDatabase;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Providers;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace EventLogExpert.Eventing.EventResolvers;

/// <summary>
///     Unified event resolver that implements a fallback chain:
///     1. MTA locale metadata files (primary, for exported logs)
///     2. SQLite provider databases (secondary)
///     3. Local provider registry (fallback)
/// </summary>
public sealed partial class EventResolver : EventResolverBase, IEventResolver
{
    private readonly Lock _databaseAccessLock = new();
    private readonly ConcurrentDictionary<string, bool> _providerFromNonLocal = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<bool>> _resolutionGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ProviderDetails?> _supplementalDetails = new(StringComparer.OrdinalIgnoreCase);

    private ImmutableArray<EventProviderDbContext> _dbContexts = [];
    private ImmutableArray<string> _metadataPaths = [];

    public EventResolver(
        IDatabaseCollectionProvider? dbCollection = null,
        IEventResolverCache? cache = null,
        ITraceLogger? logger = null) : base(cache, logger)
    {
        if (dbCollection is not null && !dbCollection.ActiveDatabases.IsEmpty)
        {
            LoadDatabases(dbCollection.ActiveDatabases);
        }

        Logger?.Debug($"{nameof(EventResolver)} was instantiated.");
    }

    public override DisplayEventModel ResolveEvent(EventRecord eventRecord)
    {
        ResolveProviderDetails(eventRecord);

        return base.ResolveEvent(eventRecord);
    }

    public void ResolveProviderDetails(EventRecord eventRecord)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, nameof(EventResolver));

        var providerName = eventRecord.ProviderName;

        // Fast path: ConcurrentDictionary is thread-safe for reads
        if (ProviderDetails.ContainsKey(providerName)) { return; }

        // Per-provider single-flight: same provider name from N threads coalesces onto one Lazy;
        // different providers resolve in parallel. Replaces the old global ProviderDetailsLock
        // that serialized every first-touch across all providers.
        var gate = _resolutionGates.GetOrAdd(
            providerName,
            static (name, self) => new Lazy<bool>(
                () => self.ResolveProviderUnderGate(name),
                LazyThreadSafetyMode.ExecutionAndPublication),
            this);

        try
        {
            _ = gate.Value;
        }
        catch
        {
            // Don't poison the provider for the resolver's lifetime — clear the gate so a later
            // call can retry. Use compare-remove so we don't yank a newer gate created mid-retry.
            RemoveGateIfSame(providerName, gate);
            throw;
        }

        // Successful resolution is now cached in ProviderDetails; the gate is no longer needed.
        // Remove it to keep _resolutionGates as a pure in-flight coordination structure.
        if (gate.IsValueCreated)
        {
            RemoveGateIfSame(providerName, gate);
        }
    }

    public void SetMetadataPaths(IReadOnlyList<string> metadataPaths) => _metadataPaths = [.. metadataPaths];

    /// <summary>
    ///     If the database file name ends in a year or a number, such as Exchange 2019 or Windows 2016, we want to sort
    ///     the database files by descending version, but by ascending product name. This generally means that databases named
    ///     for products like Exchange will be checked for matching providers first, and Windows will be checked last, with
    ///     newer versions being checked before older versions.
    /// </summary>
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
                    var versionWithoutExtension = Path.GetFileNameWithoutExtension(versionString);
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

    protected override void Dispose(bool disposing)
    {
        if (!disposing) { return; }

        ImmutableArray<EventProviderDbContext> contextsToDispose;

        using (_databaseAccessLock.EnterScope())
        {
            if (IsDisposed) { return; }

            contextsToDispose = _dbContexts;
            _dbContexts = [];
        }

        foreach (var context in contextsToDispose)
        {
            context.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override ProviderDetails? TryGetSupplementalDetails(EventRecord eventRecord)
    {
        // Only supplement providers that were loaded from MTA/DB
        if (!_providerFromNonLocal.ContainsKey(eventRecord.ProviderName)) { return null; }

        return _supplementalDetails.GetOrAdd(
            eventRecord.ProviderName,
            name =>
            {
                Logger?.Debug($"Loading supplemental local provider for {name}");

                return new EventMessageProvider(name, logger: Logger).LoadProviderDetails();
            });
    }

    [GeneratedRegex("^(.+) (\\S+)$")]
    private static partial Regex SplitProductAndVersionRegex();

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

            var providerDb = new EventProviderDbContext(file, true, Logger);
            var (needsv2, needsv3) = providerDb.IsUpgradeNeeded();

            if (needsv2 || needsv3)
            {
                obsoleteDbs.Add(file);
                providerDb.Dispose();

                continue;
            }

            providerDb.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            newContexts.Add(providerDb);
        }

        _dbContexts = [.. newContexts];

        if (obsoleteDbs.Count > 0)
        {
            foreach (var db in _dbContexts)
            {
                db.Dispose();
            }

            _dbContexts = [];

            throw new InvalidOperationException("Obsolete DB format: " +
                string.Join(' ', obsoleteDbs.Select(Path.GetFileName)));
        }
    }

    private void RemoveGateIfSame(string providerName, Lazy<bool> gate) =>
        ((ICollection<KeyValuePair<string, Lazy<bool>>>)_resolutionGates)
            .Remove(new KeyValuePair<string, Lazy<bool>>(providerName, gate));

    private void ResolveFromLocalProvider(string providerName)
    {
        var details = new EventMessageProvider(providerName, logger: Logger).LoadProviderDetails();

        ProviderDetails.TryAdd(providerName, details);
    }

    private bool ResolveProviderUnderGate(string providerName)
    {
        if (ProviderDetails.ContainsKey(providerName)) { return true; }

        // Snapshot fields once so the resolution chain sees a consistent view even if
        // SetMetadataPaths/LoadDatabases reassigns the immutable arrays mid-flight.
        var metadataPaths = _metadataPaths;
        var dbContexts = _dbContexts;

        // 1. Try MTA locale metadata files (primary source for exported logs)
        if (metadataPaths.Length > 0 && TryResolveFromMta(providerName, metadataPaths))
        {
            return true;
        }

        // 2. Try provider databases
        if (dbContexts.Length > 0 && TryResolveFromDatabase(providerName))
        {
            return true;
        }

        // 3. Fall back to local providers
        ResolveFromLocalProvider(providerName);
        return true;
    }

    private bool TryResolveFromDatabase(string providerName)
    {
        using (_databaseAccessLock.EnterScope())
        {
            ObjectDisposedException.ThrowIf(IsDisposed, nameof(EventResolver));

            foreach (var dbContext in _dbContexts)
            {
                var details = dbContext.ProviderDetails.FirstOrDefault(p =>
                    EF.Functions.Collate(p.ProviderName, "NOCASE") ==
                    EF.Functions.Collate(providerName, "NOCASE"));

                if (details is null) { continue; }

                Logger?.Debug($"Resolved {providerName} from database {dbContext.Name}.");
                ProviderDetails.TryAdd(providerName, details);
                _providerFromNonLocal.TryAdd(providerName, true);

                return true;
            }
        }

        return false;
    }

    private bool TryResolveFromMta(string providerName, ImmutableArray<string> metadataPaths)
    {
        var details = new EventMessageProvider(
            providerName,
            metadataPaths,
            Logger).LoadProviderDetails();

        if (details.Events.Count == 0 && details.Keywords.Count == 0 && details.Messages.Count == 0)
        {
            return false;
        }

        ProviderDetails.TryAdd(providerName, details);
        _providerFromNonLocal.TryAdd(providerName, true);

        return true;
    }
}
