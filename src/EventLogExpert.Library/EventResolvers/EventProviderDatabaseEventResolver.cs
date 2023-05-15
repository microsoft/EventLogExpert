// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.EventProviderDatabase;
using EventLogExpert.Library.Helpers;
using EventLogExpert.Library.Models;
using EventLogExpert.Library.Providers;
using System.Collections.Immutable;
using System.Diagnostics.Eventing.Reader;
using System.Text.RegularExpressions;

namespace EventLogExpert.Library.EventResolvers;

public class EventProviderDatabaseEventResolver : EventResolverBase, IEventResolver, IDisposable
{
    private List<EventProviderDbContext> dbContexts = new();

    private readonly string _dbFolder;

    private Dictionary<string, ProviderDetails?> _providerDetails = new();

    private bool disposedValue;

    public EventProviderDatabaseEventResolver(string databaseFolder) : this(databaseFolder, ImmutableArray<string>.Empty, s => { }) { }

    public EventProviderDatabaseEventResolver(string databaseFolder, Action<string> tracer) : this(databaseFolder, ImmutableArray<string>.Empty, tracer) { }

    public EventProviderDatabaseEventResolver(string databaseFolder, IEnumerable<string> activeDatabases) : this(databaseFolder, activeDatabases, s => { }) { }

    public EventProviderDatabaseEventResolver(string databaseFolder, IEnumerable<string> activeDatabases, Action<string> tracer) : base(tracer)
    {
        _dbFolder = databaseFolder;

        if (!Directory.Exists(_dbFolder))
        {
            Directory.CreateDirectory(_dbFolder);
        }

        AvailableDatabases = ImmutableArray<string>.Empty;

        if (activeDatabases != null && activeDatabases.Any())
        {
            ActiveDatabases = activeDatabases.ToImmutableArray();
        }
        else
        {
            ActiveDatabases = ImmutableArray<string>.Empty;
        }

        LoadDatabases();
    }

    /// <summary>
    /// Loads the databases. If ActiveDatabases is populated, any databases
    /// not named therein are skipped.
    /// </summary>
    private void LoadDatabases()
    {
        foreach (var context in dbContexts)
        {
            context.Dispose();
        }

        var allDbFiles = SortDatabases(Directory.GetFiles(_dbFolder, "*.db"));

        AvailableDatabases = allDbFiles.ToImmutableArray();

        var databasesToLoad = ActiveDatabases.Any() ? ActiveDatabases.Where(db => allDbFiles.Contains(db)) : allDbFiles;

        foreach (var file in databasesToLoad)
        {
            var c = new EventProviderDbContext(file, readOnly: true, _tracer);
            c.ChangeTracker.QueryTrackingBehavior = Microsoft.EntityFrameworkCore.QueryTrackingBehavior.NoTracking;
            dbContexts.Add(c);
        }
    }

    /// <summary>
    /// If the database file name ends in a year or a number, such as Exchange 2019 or
    /// Windows 2016, we want to sort the database files by descending version, but by
    /// ascending product name. This generally means that databases named for products
    /// like Exchange will be checked for matching providers first, and Windows will
    /// be checked last, with newer versions being checked before older versions.
    /// </summary>
    /// <param name="databaseNames"></param>
    /// <returns></returns>
    private IEnumerable<string> SortDatabases(IEnumerable<string> databaseNames)
    {
        if (databaseNames == null || !databaseNames.Any())
        {
            return Array.Empty<string>();
        }

        var r = new Regex("^(.+) (\\S+)$");

        return databaseNames
            .Select(name =>
            {
                var m = r.Match(name);
                if (m.Success)
                {
                    return new
                    {
                        FirstPart = m.Groups[1].Value + " ",
                        SecondPart = m.Groups[2].Value
                    };
                }
                else
                {
                    return new
                    {
                        FirstPart = name,
                        SecondPart = ""
                    };
                }
            })
            .OrderBy(n => n.FirstPart)
            .ThenByDescending(n => n.SecondPart)
            .Select(n => n.FirstPart + n.SecondPart)
            .ToList();
    }

    /// <summary>
    /// Changes the active databases. Note that existing database
    /// connections are closed and the provider cache is cleared.
    /// </summary>
    /// <param name="databaseNames">
    /// If this value is empty, all available databases are loaded.
    /// Otherwise, only the databases specified in this value are loaded.
    /// Also, the order of this value determines the order in which
    /// we search the databases when attempting to find a matching
    /// provider.
    /// </param>
    public void SetActiveDatabases(IEnumerable<string> databaseNames)
    {
        ActiveDatabases = databaseNames.ToImmutableArray();
        LoadDatabases();
    }

    public DisplayEventModel Resolve(EventRecord eventRecord)
    {
        DisplayEventModel lastResult = null;

        if (_providerDetails.ContainsKey(eventRecord.ProviderName))
        {
            _providerDetails.TryGetValue(eventRecord.ProviderName, out ProviderDetails? providerDetails);
            if (providerDetails != null)
            {
                lastResult = ResolveFromProviderDetails(eventRecord, providerDetails);
            }
        }
        else
        {
            foreach (var dbContext in dbContexts)
            {
                var providerDetails = dbContext.ProviderDetails.FirstOrDefault(p => p.ProviderName == eventRecord.ProviderName);
                if (providerDetails != null)
                {
                    lastResult = ResolveFromProviderDetails(eventRecord, providerDetails);

                    if (lastResult?.Description != null)
                    {
                        _tracer($"Resolved {eventRecord.ProviderName} provider from database {dbContext.Name}.");
                        _providerDetails.Add(eventRecord.ProviderName, providerDetails);
                        return lastResult;
                    }
                }
            }
        }

        if (lastResult == null)
        {
            return new DisplayEventModel(
                eventRecord.RecordId,
                eventRecord.TimeCreated?.ToUniversalTime(),
                eventRecord.Id,
                eventRecord.MachineName,
                (SeverityLevel?)eventRecord.Level,
                eventRecord.ProviderName,
                "",
                "Description not found. No provider available.",
                FormatXml(eventRecord, null));
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

            _providerDetails = null;

            disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public ImmutableArray<string> AvailableDatabases { get; private set; } = ImmutableArray<string>.Empty;

    public ImmutableArray<string> ActiveDatabases { get; private set; } = ImmutableArray<string>.Empty;
}
