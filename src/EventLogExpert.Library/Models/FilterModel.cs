namespace EventLogExpert.Library.Models;

public class FilterModel
{
    // Set to -1 to ensure filter dropdown shows "None"
    public int Id { get; set; } = -1;

    public string Description { get; set; } = string.Empty;
}
