// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.EventDatabase;
using EventLogExpert.Library.Helpers;
using EventLogExpert.Library.Models;
using System.Collections.Immutable;
using System.Diagnostics.Eventing.Reader;

namespace EventLogExpert.Library.EventResolvers;

public class EventDatabaseEventResolver : EventResolverBase, IEventResolver
{
    private List<EventDbContext> dbContexts = new();

    private readonly string dbFolder = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EventLogExpert", "Databases");

    public EventDatabaseEventResolver() : this(s => { }) { }

    public EventDatabaseEventResolver(Action<string> tracer) : base(tracer)
    {
        if (!Directory.Exists(dbFolder))
        {
            Directory.CreateDirectory(dbFolder);
        }

        var dbFiles = Directory.GetFiles(dbFolder, "*.db");
        foreach (var file in dbFiles)
        {
            var c = new EventDbContext(file, readOnly: true);
            c.ChangeTracker.QueryTrackingBehavior = Microsoft.EntityFrameworkCore.QueryTrackingBehavior.NoTracking;
            dbContexts.Add(c);
        }
    }

    public DisplayEventModel Resolve(EventRecord eventRecord)
    {
        string? description = null;
        string? taskName = null;

        foreach (var dbContext in dbContexts)
        {
            if (eventRecord.Version != null && eventRecord.LogName != null)
            {
                var modernEvents = dbContext.Events.Where(e =>
                    e.ProviderName == eventRecord.ProviderName &&
                    e.Id == eventRecord.Id &&
                    e.Version == eventRecord.Version &&
                    e.LogName == eventRecord.LogName)
                    .ToList();

                if (modernEvents != null && modernEvents.Count == 0)
                {
                    // Try again forcing the long to a short and with no log name. This is needed for providers such as Microsoft-Windows-Complus
                    modernEvents = dbContext.Events.Where(e => 
                        e.ProviderName == eventRecord.ProviderName &&
                        e.ShortId == (short)eventRecord.Id && 
                        e.Version == eventRecord.Version)
                        .ToList();
                }

                if (modernEvents != null && modernEvents.Any())
                {
                    if (modernEvents.Count > 1)
                    {
                        _tracer("Ambiguous modern event found:");
                        foreach (var modernEvent in modernEvents)
                        {
                            _tracer($"  Version: {modernEvent.Version} Id: {modernEvent.Id} LogName: {modernEvent.LogName} Description: {modernEvent.Description}");
                        }
                    }

                    var e = modernEvents[0];

                    taskName = dbContext.Tasks.FirstOrDefault(t => t.Value == e.Task)?.Name;

                    // If we don't have a description template
                    if (string.IsNullOrEmpty(e.Description))
                    {
                        // And we have exactly one property
                        if (eventRecord.Properties.Count == 1)
                        {
                            // Return that property as the description. This is what certain EventRecords look like
                            // when the entire description is a string literal, and there is no provider DLL needed.
                            description = eventRecord.Properties[0].ToString();
                        }
                        else
                        {
                            description = "This event record is missing a template. The following information was included with the event:\n\n" +
                                string.Join("\n", eventRecord.Properties);
                        }
                    }
                    else
                    {
                        description = FormatDescription(eventRecord, e.Description);
                    }
                }
            }

            if (description == null)
            {
                var potentialTaskNames = dbContext.Messages.Where(m =>
                    m.ProviderName == eventRecord.ProviderName && 
                    m.ShortId == eventRecord.Task &&
                    m.LogLink != null &&
                    m.LogLink == eventRecord.LogName)
                    .ToList();

                if (potentialTaskNames != null && potentialTaskNames.Any())
                {
                    taskName = potentialTaskNames[0].Text;

                    if (potentialTaskNames.Count > 1)
                    {
                        _tracer("More than one matching task ID was found.");
                        _tracer($"  eventRecord.Task: {eventRecord.Task}");
                        _tracer("   Potential matches:");
                        potentialTaskNames.ForEach(t => _tracer($"    {t.LogLink} {t.Text}"));
                    }
                }
                else
                {
                    taskName = eventRecord.Task == null | eventRecord.Task == 0 ? "None" : $"({eventRecord.Task})";
                }

                description = dbContext.Messages.FirstOrDefault(m => m.ProviderName == eventRecord.ProviderName && m.ShortId == eventRecord.Id)?.Text;
                description = FormatDescription(eventRecord, description);
            }

            if (description != null)
            {
                break;
            }
        }

        if (description == null)
        {
            description = "";
        }

        return new DisplayEventModel(
                eventRecord.RecordId,
                eventRecord.TimeCreated,
                eventRecord.Id,
                eventRecord.MachineName,
                (SeverityLevel?)eventRecord.Level,
                eventRecord.ProviderName,
                taskName,
                description);
    }

    public ImmutableArray<string> DatabaseNames => dbContexts.Select(c => c.Name).ToImmutableArray();
}
