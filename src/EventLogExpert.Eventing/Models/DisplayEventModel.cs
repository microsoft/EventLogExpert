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
            var sb = new StringBuilder(
            "<Event xmlns=\"http://schemas.microsoft.com/win/2004/08/events/event\">\r\n" +
            $"  <System>\r\n" +
            $"    <Provider Name=\"{Source}\" />\r\n" +
            $"    <EventID{(Qualifiers.HasValue ? $" Qualifiers=\"{Qualifiers.Value}\"" : "")}>{Id}</EventID>\r\n" +
            $"    <Level>{Level}</Level>\r\n" +
            $"    <Task>{TaskCategory}</Task>\r\n" +
            $"    <Keywords>{(Keywords.HasValue ? "0x" + Keywords.Value.ToString("X") : "0x0")}</Keywords>\r\n" +
            $"    <TimeCreated SystemTime=\"{TimeCreated.ToUniversalTime():o}\" />\r\n" +
            $"    <EventRecordID>{RecordId}</EventRecordID>\r\n");

            if (ActivityId is not null)
            {
                sb.Append($"    <ActivityID>{ActivityId}</ActivityID>\r\n");
            }
            
            sb.Append(
            $"    <Channel>{LogName}</Channel>\r\n" +
            $"    <Computer>{ComputerName}</Computer>\r\n" +
            $"    <ProcessID>{ProcessId}</ProcessID>\r\n" +
            $"    <ThreadID>{ThreadId}</ThreadID>\r\n" +
            $"  </System>\r\n" +
            $"  <EventData>\r\n");

            var templateSuccessfullyParsed = false;

            if (!string.IsNullOrEmpty(Template))
            {
                try
                {
                    var templateBuilder = new StringBuilder();

                    var propertyNames = new List<string>();
                    var index = -1;
                    while (-1 < (index = Template.IndexOf("name=", index + 1, StringComparison.OrdinalIgnoreCase)))
                    {
                        var nameStart = index + 6;
                        var nameEnd = Template.IndexOf('"', nameStart);
                        var name = Template[nameStart..nameEnd];
                        propertyNames.Add(name);
                    }

                    for (var i = 0; i < Properties.Count; i++)
                    {
                        if (i >= propertyNames.Count)
                        {
                            break;
                        }

                        if (Properties[i].Value is byte[] val)
                        {
                            templateBuilder.Append($"    <Data Name=\"{propertyNames[i]}\">{Convert.ToHexString(val)}</Data>\r\n");
                        }
                        else
                        {
                            templateBuilder.Append($"    <Data Name=\"{propertyNames[i]}\">{Properties[i].Value}</Data>\r\n");
                        }
                    }

                    sb.Append(templateBuilder);
                    templateSuccessfullyParsed = true;
                }
                catch
                {
                    // No tracer available here
                }
            }

            if (!templateSuccessfullyParsed)
            {
                foreach (var p in Properties)
                {
                    if (p.Value is byte[] bytes)
                    {
                        sb.Append($"    <Data>{Convert.ToHexString(bytes)}</Data>\r\n");
                    }
                    else
                    {
                        sb.Append($"    <Data>{p.Value}</Data>\r\n");
                    }
                }
            }

            sb.Append(
                "  </EventData>\r\n" +
                "</Event>");

            return sb.ToString();
        }
    }
}
