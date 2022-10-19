using EventLogExpert.Library.Models;
using Fluxor;

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
        List<DisplayEventModel> events,
        List<DisplayEventModel> eventsToDisplay
    )
    {
        ActiveLog = activeLog;
        Events = events;
        EventsToDisplay = eventsToDisplay;
    }

    private EventLogState() { }

    public enum LogType { Live, File }

    public LogSpecifier ActiveLog { get; } = null!;

    public List<DisplayEventModel> Events { get; } = new();

    public List<DisplayEventModel> EventsToDisplay { get; } = new();

    public record LogSpecifier(string Name, LogType? LogType);
}
