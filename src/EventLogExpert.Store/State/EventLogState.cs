using EventLogExpert.Library.Models;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Store.State;

/// <summary>
///     NOTE: Because Virtualize requires an ICollection<T>, we have to use
///     some sort of mutable collection for EventsToDisplay, unfortunately.
///     If that ever changes we should consider making these immutable.
/// </summary>
[FeatureState]
public class EventLogState
{
    public EventLogState(
        LogSpecifier activeLog,
        ICollection<DisplayEventModel> events,
        IEnumerable<Func<DisplayEventModel, bool>> filter,
        ICollection<DisplayEventModel> eventsToDisplay
    )
    {
        ActiveLog = activeLog;
        Events = events;
        Filter = filter.ToImmutableList();
        EventsToDisplay = eventsToDisplay;
    }

    private EventLogState()
    {
        ActiveLog = null;
        Events = new List<DisplayEventModel>();
        Filter = ImmutableList.Create<Func<DisplayEventModel, bool>>();
        EventsToDisplay = new List<DisplayEventModel>();
    }

    public enum LogType { Live, File }

    public LogSpecifier ActiveLog { get; }

    public ICollection<DisplayEventModel> Events { get; }

    public ICollection<DisplayEventModel> EventsToDisplay { get; }

    public ImmutableList<Func<DisplayEventModel, bool>> Filter { get; }

    public record LogSpecifier(string Name, LogType? LogType);
}
