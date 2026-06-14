// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Resolution;
using System.Collections.Concurrent;

namespace EventLogExpert.Eventing.Resolvers;

public class EventResolverBase : IDisposable
{
    protected readonly ConcurrentDictionary<string, ProviderDetails?> ProviderDetails =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IEventResolverCache? _cache;
    private readonly DescriptionFormatter _descriptions;
    private readonly ITraceLogger? _logger;
    private readonly ModernEventMatcher _matcher;
    private readonly TaskKeywordResolver _taskKeywords;
    private readonly TemplateAnalyzer _templates = new();

    private int _disposed;

    protected EventResolverBase(IEventResolverCache? cache = null, ITraceLogger? logger = null)
    {
        _cache = cache;
        _logger = logger;
        _matcher = new ModernEventMatcher(_templates, logger);
        _taskKeywords = new TaskKeywordResolver(cache, logger);
        _descriptions = new DescriptionFormatter(_templates, cache, logger, TryGetSupplementalDetails);
    }

    protected bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    protected ITraceLogger? Logger => _logger;

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public virtual ResolvedEvent ResolveEvent(EventRecord eventRecord)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        // Resolve the modern event once and reuse for both description and task name
        ProviderDetails.TryGetValue(eventRecord.ProviderName, out var details);
        var modernEvent = details is not null ? _matcher.Match(eventRecord, details) : null;

        var descriptionDetails = details;
        ProviderDetails? supplemental = null;

        // Primary is decisive when modernEvent matched or there's a single unambiguous legacy
        // message. In that case skip supplemental loading entirely.
        if (modernEvent is not null || details is null)
        {
            return CreateEventModel(eventRecord, modernEvent, details, descriptionDetails, supplemental, null);
        }

        var primaryLegacyCount = details.GetMessagesByShortId(eventRecord.Id).Count;

        if (primaryLegacyCount == 1)
        {
            return CreateEventModel(eventRecord, modernEvent, details, descriptionDetails, supplemental, null);
        }

        // Primary is non-decisive: either has no match (count == 0) or has multiple ambiguous
        // legacy messages (count > 1). Load supplemental so it's available to description,
        // task, and keyword resolution consistently. DescriptionFormatter.Resolve uses
        // supplemental as a disambiguation fallback in the count > 1 case.
        supplemental = TryGetSupplementalDetails(eventRecord);

        EventModel? supplementalModernEvent = supplemental is not null
            ? _matcher.Match(eventRecord, supplemental)
            : null;

        if (supplemental is not null && primaryLegacyCount == 0)
        {
            // Primary has no match at all — promote supplemental as the description source
            // when it matches. For count > 1, leave primary as the description source so its
            // disambiguation runs first; supplemental becomes a tiebreaker inside DescriptionFormatter.Resolve.
            if (supplementalModernEvent is not null)
            {
                modernEvent = supplementalModernEvent;
                descriptionDetails = supplemental;
            }
            else if (supplemental.GetMessagesByShortId(eventRecord.Id).Count > 0)
            {
                // Supplemental has legacy messages for this EventId
                descriptionDetails = supplemental;
            }
        }

        return CreateEventModel(eventRecord, modernEvent, details, descriptionDetails, supplemental, supplementalModernEvent);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing) { return; }

        _ = Interlocked.CompareExchange(ref _disposed, 1, 0);
    }

    /// <summary>
    ///     Override in derived classes to provide a supplemental ProviderDetails when the primary source has partial
    ///     coverage. Called from the non-decisive primary path AND as a lazy backstop during parameter resolution when the
    ///     primary has no parameter table.
    /// </summary>
    protected virtual ProviderDetails? TryGetSupplementalDetails(EventRecord eventRecord) => null;

    private ResolvedEvent CreateEventModel(
        EventRecord eventRecord,
        EventModel? modernEvent,
        ProviderDetails? details,
        ProviderDetails? descriptionDetails,
        ProviderDetails? supplemental,
        EventModel? supplementalModernEvent)
    {
        var keywords = _taskKeywords.ResolveKeywords(eventRecord, details, supplemental);

        return new(eventRecord.PathName, eventRecord.LogPathType)
        {
            ActivityId = eventRecord.ActivityId,
            ComputerName = _cache?.GetOrAddValue(eventRecord.ComputerName) ?? eventRecord.ComputerName,
            Description = _descriptions.Resolve(eventRecord, details, descriptionDetails, modernEvent, supplemental, supplementalModernEvent),
            Id = eventRecord.Id,
            Keywords = _cache?.GetOrAddKeywords(keywords) ?? keywords,
            Level = SeverityFormatter.Format(eventRecord.Level),
            LogName = _cache?.GetOrAddValue(eventRecord.LogName) ?? eventRecord.LogName,
            ProcessId = eventRecord.ProcessId,
            RecordId = eventRecord.RecordId,
            Source = _cache?.GetOrAddValue(eventRecord.ProviderName) ?? eventRecord.ProviderName,
            TaskCategory = _taskKeywords.ResolveTaskName(eventRecord, details, modernEvent, supplemental, supplementalModernEvent),
            ThreadId = eventRecord.ThreadId,
            TimeCreated = eventRecord.TimeCreated,
            UserId = _cache?.GetOrAddSid(eventRecord.UserId) ?? eventRecord.UserId,
            Xml = eventRecord.Xml ?? string.Empty
        };
    }
}
