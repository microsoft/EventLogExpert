namespace EventLogExpert.Store.Actions;

public record FilterPaneAction
{
    public record AddRecentFilter(string FilterText);
}
