namespace EventLogExpert.Store.Actions;

public record StatusBarAction
{
    public record SetEventsLoaded(int EventCount);
}
