// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.ProviderMetadata;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Lookup;
using EventLogExpert.Provider.Resolution;
using EventLogExpert.Provider.Schema;
using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace EventLogExpert.Eventing.Resolvers;

/// <summary>
///     Unified event resolver that implements a fallback chain: 1. MTA locale metadata files (primary, for exported
///     logs) 2. SQLite provider databases (secondary) 3. Local provider registry (fallback)
/// </summary>
public sealed class EventResolver : EventResolverBase, IEventResolver
{
    private readonly Lock _databaseAccessLock = new();
    private readonly IProviderDetailsLookupFactory? _lookupFactory;
    private readonly ConcurrentDictionary<string, bool> _providerFromNonLocal = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<bool>> _resolutionGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ProviderDetails?> _supplementalDetails =
        new(StringComparer.OrdinalIgnoreCase);

    private ImmutableArray<string> _metadataPaths = [];
    private ImmutableArray<IProviderDetailsLookup> _providerLookups = [];

    public EventResolver(
        IActiveDatabases? dbCollection = null,
        IEventResolverCache? cache = null,
        ITraceLogger? logger = null,
        IProviderDetailsLookupFactory? factory = null) : base(cache, logger)
    {
        _lookupFactory = factory;

        if (dbCollection is not null && !dbCollection.Paths.IsEmpty)
        {
            if (factory is null)
            {
                throw new InvalidOperationException(
                    $"An {nameof(IProviderDetailsLookupFactory)} is required when active database paths are provided.");
            }

            LoadDatabases(dbCollection.Paths);
        }

        Logger?.Debug($"{nameof(EventResolver)} was instantiated.");
    }

    public void LoadProviderDetails(EventRecord eventRecord)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

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

    public override ResolvedEvent ResolveEvent(EventRecord eventRecord)
    {
        LoadProviderDetails(eventRecord);

        return base.ResolveEvent(eventRecord);
    }

    public void SetMetadataPaths(IReadOnlyList<string> metadataPaths) => _metadataPaths = [.. metadataPaths];

    protected override void Dispose(bool disposing)
    {
        if (!disposing) { return; }

        ImmutableArray<IProviderDetailsLookup> lookupsToDispose;

        using (_databaseAccessLock.EnterScope())
        {
            if (IsDisposed) { return; }

            lookupsToDispose = _providerLookups;
            _providerLookups = [];
        }

        foreach (var lookup in lookupsToDispose)
        {
            lookup.Dispose();
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

                return new EventMessageProvider(name, logger: Logger?.ForCategory(LogCategories.ResolutionProviders))
            .LoadProviderDetails();
            });
    }

    private void LoadDatabases(IEnumerable<string> databasePaths)
    {
        Logger?.Debug($"{nameof(LoadDatabases)} was called with {databasePaths.Count()} {nameof(databasePaths)}.");

        foreach (var databasePath in databasePaths)
        {
            Logger?.Debug($"  {databasePath}");
        }

        foreach (var lookup in _providerLookups)
        {
            lookup.Dispose();
        }

        _providerLookups = [];

        var databasesToLoad = DatabasePathSorter.Sort(databasePaths);
        var unknownDbs = new List<string>();
        var obsoleteDbs = new List<string>();
        var newLookups = new List<IProviderDetailsLookup>();

        try
        {
            foreach (var file in databasesToLoad)
            {
                if (!File.Exists(file))
                {
                    throw new FileNotFoundException(file);
                }

                var lookup = _lookupFactory!.Create(file);

                try
                {
                    var state = lookup.IsUpgradeNeeded();

                    if (state.CurrentVersion == DatabaseSchemaVersion.Unknown)
                    {
                        unknownDbs.Add(file);
                        lookup.Dispose();

                        continue;
                    }

                    if (state.NeedsUpgrade)
                    {
                        obsoleteDbs.Add(file);
                        lookup.Dispose();

                        continue;
                    }

                    newLookups.Add(lookup);
                }
                catch
                {
                    lookup.Dispose();

                    throw;
                }
            }
        }
        catch
        {
            foreach (var lookup in newLookups)
            {
                lookup.Dispose();
            }

            throw;
        }

        _providerLookups = [.. newLookups];

        if (unknownDbs.Count <= 0 && obsoleteDbs.Count <= 0) { return; }

        foreach (var lookup in _providerLookups)
        {
            lookup.Dispose();
        }

        _providerLookups = [];

        var messageParts = new List<string>();

        if (unknownDbs.Count > 0)
        {
            messageParts.Add("Unrecognized DB format (file may be corrupt or from a newer or incompatible version): " +
                string.Join(' ', unknownDbs.Select(Path.GetFileName)));
        }

        if (obsoleteDbs.Count > 0)
        {
            messageParts.Add("Obsolete DB format (upgrade required): " +
                string.Join(' ', obsoleteDbs.Select(Path.GetFileName)));
        }

        throw new InvalidOperationException(string.Join(" | ", messageParts));
    }

    private void RemoveGateIfSame(string providerName, Lazy<bool> gate) =>
        ((ICollection<KeyValuePair<string, Lazy<bool>>>)_resolutionGates)
        .Remove(new KeyValuePair<string, Lazy<bool>>(providerName, gate));

    private void ResolveFromLocalProvider(string providerName)
    {
        var details = new EventMessageProvider(providerName,
            logger: Logger?.ForCategory(LogCategories.ResolutionProviders)).LoadProviderDetails();

        ProviderDetails.TryAdd(providerName, details);
    }

    private bool ResolveProviderUnderGate(string providerName)
    {
        if (ProviderDetails.ContainsKey(providerName)) { return true; }

        // Snapshot fields once so the resolution chain sees a consistent view even if
        // SetMetadataPaths/LoadDatabases reassigns the immutable arrays mid-flight.
        var metadataPaths = _metadataPaths;
        var providerLookups = _providerLookups;

        // 1. Try MTA locale metadata files (primary source for exported logs)
        if (metadataPaths.Length > 0 && TryResolveFromMta(providerName, metadataPaths))
        {
            return true;
        }

        // 2. Try provider databases
        if (providerLookups.Length > 0 && TryResolveFromDatabase(providerName, providerLookups))
        {
            return true;
        }

        // 3. Fall back to local providers
        ResolveFromLocalProvider(providerName);

        return true;
    }

    private bool TryResolveFromDatabase(string providerName, ImmutableArray<IProviderDetailsLookup> lookups)
    {
        using (_databaseAccessLock.EnterScope())
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);

            List<ProviderDetails>? candidates = null;

            foreach (var lookup in lookups)
            {
                foreach (var details in lookup.FindAllProviderVersions(providerName))
                {
                    (candidates ??= []).Add(details);
                }
            }

            if (candidates is null) { return false; }

            var resolved = ProviderVersionSelector.SelectMostComplete(candidates);

            if (resolved is null) { return false; }

            // Recency picks the newest AVAILABLE source, not the source OS of the event being resolved: an EVTX
            // record carries no OS/build signal, so this is a global preference, not a per-event match.
            Logger?.Debug(
                $"Resolved {providerName} from {candidates.Count} database version(s); winning source build " +
                $"{resolved.SourceOsBuild?.ToString() ?? "?"}.{resolved.SourceOsRevision?.ToString() ?? "?"} " +
                $"{resolved.SourceOsEdition ?? "?"} {resolved.SourceOsDisplayVersion ?? "?"}, message file version " +
                $"{resolved.MessageFileVersion ?? "?"}.");
            ProviderDetails.TryAdd(providerName, resolved);
            _providerFromNonLocal.TryAdd(providerName, true);

            return true;
        }
    }

    private bool TryResolveFromMta(string providerName, ImmutableArray<string> metadataPaths)
    {
        var details = new EventMessageProvider(
            providerName,
            metadataPaths,
            Logger?.ForCategory(LogCategories.ResolutionProviders)).LoadProviderDetails();

        if (details.Events.Count == 0 && details.Keywords.Count == 0 && details.Messages.Count == 0)
        {
            return false;
        }

        ProviderDetails.TryAdd(providerName, details);
        _providerFromNonLocal.TryAdd(providerName, true);

        return true;
    }
}
