// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.EventProviderDatabase;
using EventLogExpert.Library.Helpers;
using EventLogExpert.Library.Models;
using EventLogExpert.Library.Providers;
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventLogExpert.Library.EventResolvers;

public class EventProviderDatabaseEventResolver : EventResolverBase, IEventResolver
{
    private List<EventProviderDbContext> dbContexts = new();

    private readonly string dbFolder = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EventLogExpert", "Databases");

    private Dictionary<string, ProviderDetails?> _providerDetails = new();

    public EventProviderDatabaseEventResolver() : this(s => { }) { }

    public EventProviderDatabaseEventResolver(Action<string> tracer) : base(tracer)
    {
        if (!Directory.Exists(dbFolder))
        {
            Directory.CreateDirectory(dbFolder);
        }

        var dbFiles = Directory.GetFiles(dbFolder, "*.db");
        foreach (var file in dbFiles)
        {
            var c = new EventProviderDbContext(file, readOnly: true);
            c.ChangeTracker.QueryTrackingBehavior = Microsoft.EntityFrameworkCore.QueryTrackingBehavior.NoTracking;
            dbContexts.Add(c);
        }
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
                eventRecord.TimeCreated,
                eventRecord.Id,
                eventRecord.MachineName,
                (SeverityLevel?)eventRecord.Level,
                eventRecord.ProviderName,
                "",
                "Description not found. No provider available.");
        }

        if (lastResult.Description == null)
        {
            lastResult = lastResult with { Description = "" };
        }

        return lastResult;
    }
}
