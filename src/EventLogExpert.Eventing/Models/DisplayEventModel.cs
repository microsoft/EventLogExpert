// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Diagnostics.Eventing.Reader;
using System.Text;

namespace EventLogExpert.Eventing.Models;

public sealed record DisplayEventModel(
    long? RecordId,
    Guid? ActivityId,
    DateTime TimeCreated,
    int Id,
    string ComputerName,
    string Level,
    string Source,
    string TaskCategory,
    string Description,
    IList<EventProperty> Properties,
    int? Qualifiers,
    long? Keywords,
    IEnumerable<string> KeywordsDisplayNames,
    int? ProcessId,
    int? ThreadId,
    string LogName, // This is the log name from the event reader
    string? Template,
    string OwningLog) // This is the name of the log file or the live log, which we use internally
{
    public string Xml
    {
        get
        {
            StringBuilder sb = new();

            sb.AppendLine($"""
            <Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event">
                <System>
                    <Provider Name="{Source}" />
                    <EventID{(Qualifiers.HasValue ? $" Qualifiers=\"{Qualifiers.Value}\"" : "")}>{Id}</EventID>
                    <Level>{Level}</Level>
                    <Task>{TaskCategory}</Task>
                    <Keywords>{(Keywords.HasValue ? "0x" + Keywords.Value.ToString("X") : "0x0")}</Keywords>
                    <TimeCreated SystemTime="{TimeCreated.ToUniversalTime():o}" />
                    <EventRecordID>{RecordId}</EventRecordID>
                    {(ActivityId is null ? "<Correlation />" : $"<Correlation ActivityID=\"{ActivityId}\" />")}
                    {(ProcessId is null && ThreadId is null ?
                        "<Execution />" :
                        $"<Execution ProcessID=\"{ProcessId}\" ThreadID=\"{ThreadId}\" />")}
                    <Channel>{LogName}</Channel>
                    <Computer>{ComputerName}</Computer>
                </System>
                <EventData>
            """);

            sb.Append(GetEventData());

            sb.Append("""
                </EventData>
            </Event>
            """);

            return sb.ToString();
        }
    }

    private string GetEventData()
    {
        StringBuilder sb = new();

        if (!string.IsNullOrEmpty(Template))
        {
            try
            {
                List<string> propertyNames = [];
                int index = -1;

                while (-1 < (index = Template.IndexOf("name=", index + 1, StringComparison.Ordinal)))
                {
                    var nameStart = index + 6;
                    var nameEnd = Template.IndexOf('"', nameStart);
                    var name = Template[nameStart..nameEnd];
                    propertyNames.Add(name);
                }

                for (var i = 0; i < Properties.Count; i++)
                {
                    if (i >= propertyNames.Count) { break; }

                    if (Properties[i].Value is byte[] val)
                    {
                        sb.AppendLine($"           <Data Name=\"{propertyNames[i]}\">{Convert.ToHexString(val)}</Data>");
                    }
                    else
                    {
                        sb.AppendLine($"           <Data Name=\"{propertyNames[i]}\">{Properties[i].Value}</Data>");
                    }
                }

                return sb.ToString();
            }
            catch
            {
                // No tracer available here
            }
        }

        foreach (var p in Properties)
        {
            if (p.Value is byte[] bytes)
            {
                sb.AppendLine($"            <Data>{Convert.ToHexString(bytes)}</Data>");
            }
            else
            {
                sb.AppendLine($"            <Data>{p.Value}</Data>");
            }
        }

        return sb.ToString();
    }
}
