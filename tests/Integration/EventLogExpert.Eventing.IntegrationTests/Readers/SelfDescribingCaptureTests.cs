// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Readers;
using System.Xml;

namespace EventLogExpert.Eventing.IntegrationTests.Readers;

public sealed class SelfDescribingCaptureTests
{
    private const int InlineNameTargetCount = 5;
    private const int PerLogEvents = 400;
    private const int TotalEventBudget = 40000;

    [Fact]
    public void CapturedSelfDescribingSchema_MatchesEventXml()
    {
        int scanned = 0;
        int capturedSuccessfully = 0;
        int withInlineName = 0;

        foreach (string logName in EventLogSession.GlobalSession.GetLogNames())
        {
            if (scanned >= TotalEventBudget || withInlineName >= InlineNameTargetCount) { break; }

            EventLogReader reader;

            try
            {
                reader = new EventLogReader(logName, LogPathType.Channel, renderXml: true, reverseDirection: true, captureSelfDescribing: true);
            }
            catch
            {
                continue;
            }

            using (reader)
            {
                if (!reader.IsValid) { continue; }

                int readForThisLog = 0;

                while (readForThisLog < PerLogEvents &&
                       scanned < TotalEventBudget &&
                       reader.TryGetEvents(out EventRecord[] batch, batchSize: 200) &&
                       batch.Length > 0)
                {
                    foreach (EventRecord record in batch)
                    {
                        if (readForThisLog >= PerLogEvents) { break; }

                        readForThisLog++;
                        scanned++;

                        // An exception anywhere in the render (including RenderSelfDescribingName) is caught per event and
                        // surfaces as an unsuccessful record, so a successful record proves the capture path ran.
                        if (!record.IsSuccess) { continue; }

                        capturedSuccessfully++;

                        if (string.IsNullOrEmpty(record.Xml)) { continue; }

                        (string? expectedName, List<string> expectedFieldNames) = ParseEventDataSchema(record.Xml);

                        // The value-path detection must agree with the event's XML: a named <EventData> yields that
                        // name, an unnamed one (classic / manifest without a template name) yields null and no fields.
                        Assert.Equal(expectedName, record.SelfDescribingName);

                        if (expectedName is null)
                        {
                            Assert.True(record.SelfDescribingFieldNames.IsDefaultOrEmpty, $"Unexpected field names captured for an unnamed EventData in '{logName}'.");

                            continue;
                        }

                        withInlineName++;
                        Assert.Equal(expectedFieldNames, record.SelfDescribingFieldNames);
                    }
                }
            }
        }

        Assert.True(scanned > 0, "No events were available on any channel to scan.");

        // Fail rather than skip if capture produced no successfully-read records: a value-path or native-conversion
        // regression that throws for every event would otherwise mark all records unsuccessful, skip every invariant
        // above, and leave the test silently skipped on the no-inline-name guard below.
        Assert.True(capturedSuccessfully > 0, "captureSelfDescribing produced no successfully-read records; the capture path may be throwing for every event.");

        // Some hosts / containers carry only classic events, so a self-describing event may be absent. The invariant
        // above still guards the negative case on every scanned event; the positive path is checked when one is present.
        Assert.SkipUnless(withInlineName > 0, "No event with an inline <EventData Name='...'> was present to exercise the positive path.");
    }

    private static string? AttributeByLocalName(XmlElement element, string localName)
    {
        foreach (XmlAttribute attribute in element.Attributes)
        {
            if (attribute.LocalName == localName) { return attribute.Value; }
        }

        return null;
    }

    private static XmlElement? DirectChildByLocalName(XmlElement parent, string localName)
    {
        foreach (XmlNode child in parent.ChildNodes)
        {
            if (child is XmlElement element && element.LocalName == localName) { return element; }
        }

        return null;
    }

    // Local-name parse (namespace-agnostic, matching the values-render behavior) of the first top-level <EventData>:
    // returns its Name attribute (or null) and, when named, the ordered Name of each child <Data> ("" when unnamed).
    private static (string? Name, List<string> FieldNames) ParseEventDataSchema(string xml)
    {
        var document = new XmlDocument();
        document.LoadXml(xml);

        XmlElement? root = document.DocumentElement;
        var fieldNames = new List<string>();

        if (root is null) { return (null, fieldNames); }

        XmlElement? eventData = DirectChildByLocalName(root, "EventData");

        if (eventData is null) { return (null, fieldNames); }

        string? name = AttributeByLocalName(eventData, "Name");

        if (name is null) { return (null, fieldNames); }

        foreach (XmlNode child in eventData.ChildNodes)
        {
            if (child is XmlElement element && element.LocalName == "Data")
            {
                fieldNames.Add(AttributeByLocalName(element, "Name") ?? string.Empty);
            }
        }

        return (name, fieldNames);
    }
}
