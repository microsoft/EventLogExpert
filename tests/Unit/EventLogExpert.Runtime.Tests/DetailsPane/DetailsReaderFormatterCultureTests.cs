// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Structured;
using EventLogExpert.Eventing.TestUtils;
using EventLogExpert.Runtime.Common.Display;
using EventLogExpert.Runtime.DetailsPane;
using EventLogExpert.Runtime.Tests.TestUtils;
using System.Globalization;

namespace EventLogExpert.Runtime.Tests.DetailsPane;

/// <summary>
///     Pins the clipboard copy path's structural English labels and its numeric field values as
///     <see cref="CultureInfo.CurrentCulture" />-independent, so a later Runtime localization increment cannot silently
///     make copied text vary with the OS regional culture.
/// </summary>
/// <remarks>
///     Scope is the CurrentCulture axis only. <see cref="DetailsReaderFormatter.BuildEventCopyText" /> is NOT
///     byte-invariant because its "Date and Time" VALUE is CurrentCulture-formatted by design (documented, deferred - see
///     the <c>loc-copyexport-value-invariance</c> follow-up); only its labels are pinned. Runtime remains independent of
///     UI culture and localizer injection. Contrast culture is <c>fi-FI</c> (see <c>EventTableExporterCultureTests</c> for
///     why not <c>de-DE</c>).
/// </remarks>
[Collection(CultureSensitiveCollection.Name)]
public sealed class DetailsReaderFormatterCultureTests
{
    private static readonly CultureInfo s_contrast = CultureInfo.GetCultureInfo("fi-FI");
    private static readonly CultureInfo s_english = CultureInfo.GetCultureInfo("en-US");
    private static readonly TimeZoneInfo s_plusThree = TimeZoneInfo.CreateCustomTimeZone(
        "Slice3PlusThree",
        TimeSpan.FromHours(3),
        "Slice3PlusThree",
        "Slice3PlusThree");

    [Fact]
    public void BuildEventCopyText_DateValueUsesCurrentCulture()
    {
        ResolvedEvent @event = EventDataTestFactory.CreateEventWithData(("LogonType", 3)) with
        {
            TimeCreated = new DateTime(2026, 8, 26, 17, 57, 5, DateTimeKind.Utc)
        };

        string english = RunUnderCulture(s_english, () => DetailsReaderFormatter.BuildEventCopyText(Model(@event)));
        string contrast = RunUnderCulture(s_contrast, () => DetailsReaderFormatter.BuildEventCopyText(Model(@event)));

        Assert.Contains(@event.TimeCreated.ToString(s_english), english, StringComparison.Ordinal);
        Assert.Contains(@event.TimeCreated.ToString(s_contrast), contrast, StringComparison.Ordinal);
        Assert.NotEqual(english, contrast);
    }

    [Fact]
    public void BuildEventCopyText_EmitsEnglishStructuralLabels_UnderForeignCulture()
    {
        // The fixture populates EVERY conditionally-emitted section (Source, Level, Message, EventData, UserData) so a
        // missing section fails loudly rather than silently skipping its label assertion.
        ResolvedEvent @event = EventDataTestFactory.CreateEventWithData(("LogonType", 3)) with
        {
            Id = 4624,
            Level = "Warning",
            Source = "Contoso",
            Description = "A message.",
            UserData = [new UserDataField("Config/Setting", ["u1"], false)]
        };

        string copy = RunUnderCulture(s_contrast, () => DetailsReaderFormatter.BuildEventCopyText(Model(@event)));

        string[] labels = ["Event ID:", "Level:", "Source:", "Date and Time:", "Message:", "Event Data:", "User Data:"];
        foreach (string label in labels)
        {
            Assert.Contains(label, copy, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BuildEventCopyText_EmitsEnglishStructuralLabels_UnderForeignUiCulture()
    {
        ResolvedEvent @event = EventDataTestFactory.CreateEventWithData(("LogonType", 3)) with
        {
            Id = 4624,
            Level = "Warning",
            Source = "Contoso",
            TimeCreated = new DateTime(2026, 8, 26, 17, 57, 5, DateTimeKind.Utc)
        };

        string copy = RunUnderCultureAndUiCulture(CultureInfo.GetCultureInfo("de-DE"), () => DetailsReaderFormatter.BuildEventCopyText(Model(@event)));

        Assert.Contains("Source:", copy, StringComparison.Ordinal);
        Assert.Contains("Date and Time:", copy, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPropertiesCopyText_EmitsEnglishHeaderLabels_UnderForeignCulture()
    {
        ResolvedEvent @event = new ResolvedEvent("TestLog", LogPathType.Channel) with
        {
            Source = "Contoso",
            ComputerName = "HOST01",
            LogName = "Application",
            TimeCreated = new DateTime(2026, 8, 26, 17, 57, 5, DateTimeKind.Utc)
        };

        string copy = RunUnderCulture(s_contrast, () => DetailsReaderFormatter.BuildPropertiesCopyText(Model(@event).Header));

        string[] labels = ["Source:", "Date and Time:", "Computer:", "Log Name:"];
        foreach (string label in labels)
        {
            Assert.Contains(label, copy, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EventDataArray_DisplayItemsUseCurrentCultureAndCopyItemsStayInvariant()
    {
        ResolvedEvent @event = EventDataTestFactory.CreateEventWithData(("Ratios", new[] { 1.5, 2.25 }));

        DetailsField field = RunUnderCulture(s_contrast, () => EventDataField(@event, "Ratios"));

        Assert.Equal([1.5.ToString(s_contrast), 2.25.ToString(s_contrast)], field.PreviewLines);
        Assert.Equal("1.5\n2.25", field.CopyValue);
        Assert.Contains(s_contrast.NumberFormat.NumberDecimalSeparator, field.PreviewLines[0], StringComparison.Ordinal);
        Assert.DoesNotContain(s_contrast.NumberFormat.NumberDecimalSeparator, field.CopyValue, StringComparison.Ordinal);
    }

    [Fact]
    public void EventDataDateTimeArray_DisplayItemsUseCurrentCultureInTimeZoneAndCopyItemsRoundTrip()
    {
        // Sub-second ticks discriminate the round-trippable "O" copy format from the general invariant format, which
        // would silently drop them (and DateTimeKind), leaving a copied DateTime[] unable to round-trip.
        var first = new DateTime(2026, 8, 26, 17, 57, 5, 123, DateTimeKind.Utc).AddTicks(4567);
        var second = new DateTime(2026, 8, 26, 18, 2, 3, 456, DateTimeKind.Utc).AddTicks(7890);
        ResolvedEvent @event = EventDataTestFactory.CreateEventWithData(("Times", new[] { first, second }));

        DetailsField field = RunUnderCulture(s_contrast, () => EventDataField(@event, "Times", s_plusThree));

        Assert.Equal(
            [first.ConvertTimeZone(s_plusThree).ToString(s_contrast), second.ConvertTimeZone(s_plusThree).ToString(s_contrast)],
            field.PreviewLines);

        string[] copyLines = field.CopyValue.Split('\n');
        Assert.Equal(first.ToString("O", CultureInfo.InvariantCulture), copyLines[0]);
        Assert.Equal(second.ToString("O", CultureInfo.InvariantCulture), copyLines[1]);

        DateTime firstRoundTrip = DateTime.ParseExact(copyLines[0], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        DateTime secondRoundTrip = DateTime.ParseExact(copyLines[1], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        Assert.Equal(first, firstRoundTrip);
        Assert.Equal(second, secondRoundTrip);

        // DateTime equality ignores Kind, so pin it explicitly: the whole point of "O" is preserving DateTimeKind.
        Assert.Equal(DateTimeKind.Utc, firstRoundTrip.Kind);
        Assert.Equal(DateTimeKind.Utc, secondRoundTrip.Kind);
    }

    [Fact]
    public void EventDataDateTime_DisplayUsesCurrentCultureInTimeZoneAndCopyUsesOriginalRoundTrip()
    {
        var timestamp = new DateTime(2026, 8, 26, 17, 57, 5, 123, DateTimeKind.Utc).AddTicks(4567);
        ResolvedEvent @event = EventDataTestFactory.CreateEventWithData(("SeenAt", timestamp));

        DetailsField field = RunUnderCulture(s_contrast, () => EventDataField(@event, "SeenAt", s_plusThree));
        DateTime converted = timestamp.ConvertTimeZone(s_plusThree);

        Assert.Equal(converted.ToString(s_contrast), field.PreviewLines[0]);
        Assert.Equal(timestamp.ToString("O", CultureInfo.InvariantCulture), field.CopyValue);
        Assert.Equal(
            timestamp,
            DateTime.ParseExact(field.CopyValue, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    [Fact]
    public void EventDataDouble_DisplayUsesCurrentCultureAndCopyStaysInvariant()
    {
        ResolvedEvent @event = EventDataTestFactory.CreateEventWithData(("Ratio", 1.5), ("Account", "CONTOSO\\alice"));

        DetailsField field = RunUnderCulture(s_contrast, () => EventDataField(@event, "Ratio"));
        string copy = RunUnderCulture(s_contrast, () => DetailsReaderFormatter.BuildFieldsCopyText(Model(@event).EventData));

        Assert.Contains(s_contrast.NumberFormat.NumberDecimalSeparator, field.PreviewLines[0], StringComparison.Ordinal);
        Assert.Equal(1.5.ToString(s_contrast), field.PreviewLines[0]);
        Assert.Equal("1.5", field.CopyValue);
        Assert.Contains("1.5", copy, StringComparison.Ordinal);
        Assert.DoesNotContain(1.5.ToString(s_contrast), copy, StringComparison.Ordinal);
    }

    [Fact]
    public void EventDataInt64_DisplayUsesCurrentCultureNegativeSignAndCopyStaysInvariant()
    {
        CultureInfo culture = CultureInfo.GetCultureInfo("fi-FI").Clone() as CultureInfo ?? throw new InvalidOperationException("Expected cloneable culture.");
        culture.NumberFormat.NegativeSign = "[MINUS]";
        ResolvedEvent @event = EventDataTestFactory.CreateEventWithData(("Counter", -1234567L));

        DetailsField field = RunUnderCulture(culture, () => EventDataField(@event, "Counter"));

        Assert.Contains(culture.NumberFormat.NegativeSign, field.PreviewLines[0], StringComparison.Ordinal);
        Assert.Equal((-1234567L).ToString(culture), field.PreviewLines[0]);
        Assert.Equal((-1234567L).ToString(CultureInfo.InvariantCulture), field.CopyValue);
        Assert.Contains("-", field.CopyValue, StringComparison.Ordinal);
    }

    [Fact]
    public void EventDataInt64_DisplayUsesCurrentCultureWithoutGrouping()
    {
        ResolvedEvent @event = EventDataTestFactory.CreateEventWithData(("Counter", 1234567L));

        DetailsField field = RunUnderCulture(s_contrast, () => EventDataField(@event, "Counter"));

        Assert.Equal(1234567L.ToString(s_contrast), field.PreviewLines[0]);
        Assert.DoesNotContain(s_contrast.NumberFormat.NumberGroupSeparator, field.PreviewLines[0], StringComparison.Ordinal);
        Assert.Equal(1234567L.ToString(CultureInfo.InvariantCulture), field.CopyValue);
    }

    private static DetailsField EventDataField(ResolvedEvent @event, string label, TimeZoneInfo? timeZone = null) =>
        Assert.Single(DetailsReaderFormatter.BuildModel(@event, timeZone ?? TimeZoneInfo.Utc).EventData, field => string.Equals(field.Label, label, StringComparison.Ordinal));

    private static DetailsReaderModel Model(ResolvedEvent @event) => DetailsReaderFormatter.BuildModel(@event, TimeZoneInfo.Utc);

    private static T RunUnderCulture<T>(CultureInfo culture, Func<T> build)
    {
        CultureInfo priorCulture = CultureInfo.CurrentCulture;
        CultureInfo priorUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en"); // isolate the regional axis from the localization axis
            return build();
        }
        finally
        {
            CultureInfo.CurrentCulture = priorCulture;
            CultureInfo.CurrentUICulture = priorUiCulture;
        }
    }

    private static string RunUnderCultureAndUiCulture(CultureInfo culture, Func<string> build)
    {
        CultureInfo priorCulture = CultureInfo.CurrentCulture;
        CultureInfo priorUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            return build();
        }
        finally
        {
            CultureInfo.CurrentCulture = priorCulture;
            CultureInfo.CurrentUICulture = priorUiCulture;
        }
    }
}
