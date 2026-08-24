// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Structured;
using System.Text;

namespace EventLogExpert.Eventing.Tests.Structured;

// Guards the self-describing (TraceLogging) field-name scan: pull the ordered <Data Name='...'> labels from an event's
// XML so the resolver can pair them with the already-rendered field values. The event name and the decision to render
// XML at all are handled upstream (a cheap values render of <EventData Name>); this extractor only supplies labels.
public sealed class SelfDescribingFieldNameExtractorTests
{
    private const string Ns = "xmlns='http://schemas.microsoft.com/win/2004/08/events/event'";

    [Fact]
    public void Extract_FieldCountExceedingCap_StopsAtCap()
    {
        var builder = new StringBuilder($"<Event {Ns}><EventData Name='E'>");

        for (int i = 0; i < SelfDescribingFieldNameExtractor.MaxFieldNames + 25; i++)
        {
            builder.Append($"<Data Name='F{i}'>{i}</Data>");
        }

        builder.Append("</EventData></Event>");

        Assert.Equal(SelfDescribingFieldNameExtractor.MaxFieldNames, SelfDescribingFieldNameExtractor.Extract(builder.ToString()).Length);
    }

    [Fact]
    public void Extract_NamedFields_AreReturnedInDocumentOrder()
    {
        var names = SelfDescribingFieldNameExtractor.Extract(
            $"<Event {Ns}><System><EventID>0</EventID></System>" +
            "<EventData Name='WER_PAYLOAD_HEALTH_FAIL'>" +
            "<Data Name='Stage'>s1event</Data>" +
            "<Data Name='BytesUploaded'>19387</Data>" +
            "</EventData></Event>");

        Assert.Equal(["Stage", "BytesUploaded"], names);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Extract_NullOrEmptyXml_YieldsNoNames(string? xml) =>
        Assert.True(SelfDescribingFieldNameExtractor.Extract(xml).IsEmpty);

    [Fact]
    public void Extract_SelfClosingData_ContributesItsLabel()
    {
        var names = SelfDescribingFieldNameExtractor.Extract(
            $"<Event {Ns}><EventData Name='E'><Data Name='Empty'/><Data Name='Set'>v</Data></EventData></Event>");

        Assert.Equal(["Empty", "Set"], names);
    }

    [Fact]
    public void Extract_UnnamedData_ContributesAnEmptyLabel()
    {
        var names = SelfDescribingFieldNameExtractor.Extract(
            $"<Event {Ns}><EventData Name='E'><Data>positional</Data><Data Name='Set'>v</Data></EventData></Event>");

        Assert.Equal(["", "Set"], names);
    }

    [Fact]
    public void Extract_UserDataEnvelope_YieldsNoNames()
    {
        // Nested UserData is a separate concern; only <EventData> Data labels are self-describing field names.
        var names = SelfDescribingFieldNameExtractor.Extract(
            $"<Event {Ns}><UserData><Payload><Field Name='x'>1</Field></Payload></UserData></Event>");

        Assert.True(names.IsEmpty);
    }

    [Fact]
    public void Extract_XmlEntitiesInName_AreDecoded()
    {
        var names = SelfDescribingFieldNameExtractor.Extract(
            $"<Event {Ns}><EventData Name='E'><Data Name='A &amp; B'>v</Data></EventData></Event>");

        Assert.Equal(["A & B"], names);
    }
}
