// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Helpers;
using System.Diagnostics.Eventing.Reader;
using System.Text;

namespace EventLogExpert.Library.Models;

public record DisplayEventModel(
    long? RecordId,
    DateTime TimeCreated,
    int Id,
    string ComputerName,
    SeverityLevel? Level,
    string Source,
    string TaskCategory,
    string Description,
    IList<EventProperty> Properties,
    int? Qualifiers,
    long? Keywords,
    IEnumerable<string> KeywordsDisplayNames,
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
            $"    <Keywords>{(Keywords.HasValue ? ("0x" + Keywords.Value.ToString("X")) : "0x0")}</Keywords>\r\n" +
            $"    <TimeCreated SystemTime=\"{TimeCreated.ToUniversalTime():o}\" />\r\n" +
            $"    <EventRecordID>{RecordId}</EventRecordID>\r\n" +
            $"    <Channel>{LogName}</Channel>\r\n" +
            $"    <Computer>{ComputerName}</Computer>\r\n" +
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
                    while (-1 < (index = Template.IndexOf("name=", index + 1)))
                    {
                        var nameStart = index + 6;
                        var nameEnd = Template.IndexOf('"', nameStart);
                        var name = Template.Substring(nameStart, nameEnd - nameStart);
                        propertyNames.Add(name);
                    }

                    for (var i = 0; i < Properties.Count; i++)
                    {
                        if (i >= propertyNames.Count)
                        {
                            break;
                        }

                        if (propertyNames[i] == "__binLength" && propertyNames[i + 1] == "BinaryData" && Properties[i].Value is byte[] val)
                        {
                            // Handle event 7036 from Service Control Manager binary data
                            templateBuilder.Append($"    <Data Name=\"{propertyNames[i]}\">{val.Length}</Data>\r\n");
                            templateBuilder.Append($"    <Data Name=\"{propertyNames[i + 1]}\">{Convert.ToHexString(val)}</Data>\r\n");
                            i++;
                        }
                        else
                        {
                            templateBuilder.Append($"    <Data Name=\"{propertyNames[i]}\">{Properties[i].Value}</Data>\r\n");
                        }
                    }

                    sb.Append(templateBuilder.ToString());
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
