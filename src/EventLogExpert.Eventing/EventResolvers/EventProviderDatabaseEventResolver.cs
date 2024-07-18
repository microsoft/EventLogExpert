﻿// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventProviderDatabase;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Providers;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.Eventing.Reader;
using System.Text.RegularExpressions;

namespace EventLogExpert.Eventing.EventResolvers;

public partial class EventProviderDatabaseEventResolver : EventResolverBase, IEventResolver
{
    public string Status { get; private set; } = string.Empty;

    public event EventHandler<string>? StatusChanged;

    private ImmutableArray<EventProviderDbContext> _dbContexts = [];

    private readonly ConcurrentDictionary<string, ProviderDetails?> _providerDetails = new();

    private readonly SemaphoreSlim _databaseAccessSemaphore = new(1);

    private bool _disposedValue;

    public EventProviderDatabaseEventResolver(IDatabaseCollectionProvider dbCollection) : this(dbCollection, (s, log) => { }) { }

    public EventProviderDatabaseEventResolver(IDatabaseCollectionProvider dbCollection, Action<string, LogLevel> tracer) : base(tracer)
    {
        tracer($"{nameof(EventProviderDatabaseEventResolver)} was instantiated at:\n{Environment.StackTrace}", LogLevel.Information);
        LoadDatabases(dbCollection.ActiveDatabases);
    }

    /// <summary>
    /// Loads the databases. If ActiveDatabases is populated, any databases
    /// not named therein are skipped.
    /// </summary>
    private void LoadDatabases(IEnumerable<string> databasePaths)
    {
        _tracer($"{nameof(LoadDatabases)} was called with {databasePaths.Count()} {nameof(databasePaths)}.", LogLevel.Information);
        foreach (var databasePath in databasePaths)
        {
            _tracer($"  {databasePath}", LogLevel.Information);
        }

        _providerDetails.Clear();

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

            var c = new EventProviderDbContext(file, readOnly: true, _tracer);
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
            .Select(n => Path.Join(n.Directory, n.FirstPart + n.SecondPart));
    }

    public DisplayEventModel Resolve(EventRecord eventRecord, string owningLogName)
    {
        DisplayEventModel? lastResult = null;

        // The Properties getter is expensive, so we only call the getter once,
        // and we pass this value separately from the eventRecord so it can be reused.
        var eventProperties = eventRecord.Properties;

        if (_providerDetails.TryGetValue(eventRecord.ProviderName, out var providerDetails))
        {
            if (providerDetails is not null)
            {
                lastResult = ResolveFromProviderDetails(eventRecord, eventProperties, providerDetails, owningLogName);
            }
        }
        else
        {
            _databaseAccessSemaphore.Wait();

            try
            {
                foreach (var dbContext in _dbContexts)
                {
                    providerDetails = dbContext.ProviderDetails.FirstOrDefault(p => p.ProviderName.ToLower() == eventRecord.ProviderName.ToLower());

                    if (providerDetails is null) { continue; }

                    lastResult = ResolveFromProviderDetails(eventRecord, eventProperties, providerDetails, owningLogName);

                    if (!string.IsNullOrEmpty(lastResult.Description))
                    {
                        _tracer($"Resolved {eventRecord.ProviderName} provider from database {dbContext.Name}.", LogLevel.Information);
                        _providerDetails.TryAdd(eventRecord.ProviderName, providerDetails);

                        return lastResult;
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

        if (lastResult is null)
        {
            return new DisplayEventModel(
                eventRecord.RecordId,
                eventRecord.ActivityId,
                eventRecord.TimeCreated!.Value.ToUniversalTime(),
                eventRecord.Id,
                eventRecord.MachineName,
                Severity.GetString(eventRecord.Level),
                eventRecord.ProviderName,
                "",
                "Description not found. No provider available.",
                eventRecord.Qualifiers,
                GetKeywordsFromBitmask(eventRecord.Keywords, null),
                eventRecord.ProcessId,
                eventRecord.ThreadId,
                eventRecord.UserId,
                eventRecord.LogName,
                owningLogName,
                eventRecord.ToXml());
        }

        return lastResult;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                foreach (var context in _dbContexts)
                {
                    context.Dispose();
                }
            }

            _providerDetails.Clear();

            _disposedValue = true;

            _tracer($"{nameof(EventProviderDatabaseEventResolver)} Disposed at:\n{Environment.StackTrace}", LogLevel.Information);
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    [GeneratedRegex("^(.+) (\\S+)$")]
    private static partial Regex SplitProductAndVersionRegex();
}
