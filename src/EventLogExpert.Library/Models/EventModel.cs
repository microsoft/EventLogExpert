namespace EventLogExpert.Library.Models;

public class EventModel
{
    public long Id { get; set; }

    public byte Version { get; set; }

    public string LogName { get; set; }

    public int Level { get; set; }

    public int Opcode { get; set; }

    public int Task { get; set; }

    public long[] Keywords { get; set; }

    public string Template { get; set; }

    public string Description { get; set; }
}
