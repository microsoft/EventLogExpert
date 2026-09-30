// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Tests.TestUtils;
using NSubstitute;
using System.Collections.Immutable;
using System.Globalization;

namespace EventLogExpert.Runtime.Tests.Common.Clipboard;

[Collection(CultureSensitiveCollection.Name)]
public sealed class EventCopyFormatterCultureTests
{
    private static readonly ImmutableDictionary<ColumnName, bool> s_columns = ImmutableDictionary<ColumnName, bool>.Empty
        .Add(ColumnName.Level, true)
        .Add(ColumnName.Source, true)
        .Add(ColumnName.EventId, true);
    private static readonly EventLogId s_logId = EventLogId.Create();
    private static readonly ImmutableList<ColumnName> s_order =
        ImmutableList.Create(ColumnName.Level, ColumnName.Source, ColumnName.EventId);

    [Theory]
    [InlineData(EventCopyFormat.Simple)]
    [InlineData(EventCopyFormat.Full)]
    public async Task FormatAsync_EventIdDoesNotGroupLargeValues_UnderForeignCulture(EventCopyFormat format)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo("fi-FI");

        string result = await FormatUnderCultureAsync(culture, format, id: 1234567);

        Assert.Contains("1234567", result, StringComparison.Ordinal);
        Assert.DoesNotContain("1" + culture.NumberFormat.NumberGroupSeparator + "234", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(EventCopyFormat.Simple)]
    [InlineData(EventCopyFormat.Full)]
    public async Task FormatAsync_EventIdUsesInvariantNegativeSign_UnderCustomCulture(EventCopyFormat format)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo("fi-FI").Clone() as CultureInfo ?? throw new InvalidOperationException("Expected cloneable culture.");
        culture.NumberFormat.NegativeSign = "[MINUS]";

        string result = await FormatUnderCultureAsync(culture, format, id: -1234567);

        Assert.Contains("-1234567", result, StringComparison.Ordinal);
        Assert.DoesNotContain(culture.NumberFormat.NegativeSign, result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FormatAsync_FullFormat_RoutesCultureSensitiveDateThroughCopyText() =>
        Assert.Contains("[[Date(26.8.2026 17.57.05)]]", await FormatUnderContrastCultureAsync(EventCopyFormat.Full), StringComparison.Ordinal);

    [Fact]
    public async Task FormatAsync_MarkdownFormat_RoutesDescriptionHeaderThroughCopyText()
    {
        string result = await FormatUnderContrastCultureAsync(EventCopyFormat.Markdown);

        Assert.StartsWith("|", result, StringComparison.Ordinal);
        Assert.Contains("| [[Column_Level]] | [[Column_Source]] | [[Column_EventId]] | [[MarkdownDescriptionHeader]] |", result, StringComparison.Ordinal);
    }

    private static IEventCopyText CopyText() => new MarkerEventCopyText();

    private static SelectionEntry Entry(EventLocator locator) => new(locator, locator, null);

    private static Task<string> FormatUnderContrastCultureAsync(EventCopyFormat format) =>
        FormatUnderCultureAsync(CultureInfo.GetCultureInfo("fi-FI"), format, id: 4000);

    private static async Task<string> FormatUnderCultureAsync(CultureInfo culture, EventCopyFormat format, int id)
    {
        var locator = new EventLocator(s_logId, 0, 0);
        var @event = new ResolvedEvent("Application", LogPathType.Channel)
        {
            RecordId = 1,
            Id = id,
            Source = "ProviderA",
            Description = "Alpha",
            TimeCreated = new DateTime(2026, 8, 26, 17, 57, 5, DateTimeKind.Utc)
        };

        var detailResolver = Substitute.For<IEventDetailResolver>();
        detailResolver.TryResolve(locator, out Arg.Any<ResolvedEvent?>())
            .Returns(call => { call[1] = @event; return true; });

        var formatter = new EventCopyFormatter(detailResolver, Substitute.For<IEventXmlResolver>(), CopyText());

        return await RunUnderCultureAsync(
            culture,
            () => formatter.FormatAsync(
                new EventCopyRequest([Entry(locator)], null, s_columns, s_order, format, TimeZoneInfo.Utc),
                TestContext.Current.CancellationToken));
    }

    private static async Task<string> RunUnderCultureAsync(CultureInfo culture, Func<Task<string>> build)
    {
        CultureInfo priorCulture = CultureInfo.CurrentCulture;
        CultureInfo priorUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en"); // isolate the regional axis from the localization axis
            return await build();
        }
        finally
        {
            CultureInfo.CurrentCulture = priorCulture;
            CultureInfo.CurrentUICulture = priorUiCulture;
        }
    }

    private sealed class MarkerEventCopyText : IEventCopyText
    {
        public string MarkdownDescriptionHeader => "[[MarkdownDescriptionHeader]]";

        public string FieldLine(EventCopyFullField field, string value) => field switch
        {
            EventCopyFullField.DescriptionHeader => "[[DescriptionHeader]]",
            EventCopyFullField.EventXmlHeader => "[[EventXmlHeader]]",
            _ => $"[[{field}({value})]]"
        };

        public string MarkdownColumnHeader(ColumnName column) => $"[[Column_{column}]]";
    }
}
