// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Structured;
using EventLogExpert.Eventing.TestUtils;
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
    public void BuildFieldsCopyText_NumericFieldValues_StayInvariant_UnderForeignCulture()
    {
        // A double whose invariant rendering ("1.5") differs from fi-FI's ("1,5" decimal comma): today the field value
        // routes through EventFieldValue.AsString (InvariantCulture), so both captures are byte-identical; if that ever
        // regressed to CurrentCulture the fi-FI capture would diverge and fail this guard. A bare positive integer would
        // format identically under both cultures and could not detect that regression.
        ResolvedEvent @event = EventDataTestFactory.CreateEventWithData(("Ratio", 1.5), ("Account", "CONTOSO\\alice"));

        string english = RunUnderCulture(s_english, () => DetailsReaderFormatter.BuildFieldsCopyText(Model(@event).EventData));
        string contrast = RunUnderCulture(s_contrast, () => DetailsReaderFormatter.BuildFieldsCopyText(Model(@event).EventData));

        Assert.Equal(english, contrast);
        Assert.Contains("1.5", english, StringComparison.Ordinal); // the culture-varying value is actually present
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

    private static DetailsReaderModel Model(ResolvedEvent @event) => DetailsReaderFormatter.BuildModel(@event, TimeZoneInfo.Utc);

    private static string RunUnderCulture(CultureInfo culture, Func<string> build)
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
