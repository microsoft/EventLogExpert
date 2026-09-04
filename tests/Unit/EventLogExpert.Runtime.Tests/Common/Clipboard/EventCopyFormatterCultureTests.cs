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

    [Fact]
    public async Task FormatAsync_FullFormat_RoutesCultureSensitiveDateThroughCopyText() =>
        Assert.Contains("[[Date(26.8.2026 17.57.05)]]", await FormatUnderContrastCultureAsync(EventCopyFormat.Full), StringComparison.Ordinal);

    [Fact]
    public async Task FormatAsync_MarkdownFormat_RoutesDescriptionHeaderThroughCopyText()
    {
        string result = await FormatUnderContrastCultureAsync(EventCopyFormat.Markdown);

        Assert.StartsWith("|", result, StringComparison.Ordinal);
        Assert.Contains("| Level | Source | Event ID | [[MarkdownDescriptionHeader]] |", result, StringComparison.Ordinal);
    }

    private static IEventCopyText CopyText() => new MarkerEventCopyText();

    private static SelectionEntry Entry(EventLocator locator) => new(locator, locator, null);

    private static async Task<string> FormatUnderContrastCultureAsync(EventCopyFormat format)
    {
        var locator = new EventLocator(s_logId, 0, 0);
        var @event = new ResolvedEvent("Application", LogPathType.Channel)
        {
            RecordId = 1,
            Id = 4000,
            Source = "ProviderA",
            Description = "Alpha",
            TimeCreated = new DateTime(2026, 8, 26, 17, 57, 5, DateTimeKind.Utc)
        };

        var detailResolver = Substitute.For<IEventDetailResolver>();
        detailResolver.TryResolve(locator, out Arg.Any<ResolvedEvent?>())
            .Returns(call => { call[1] = @event; return true; });

        var formatter = new EventCopyFormatter(detailResolver, Substitute.For<IEventXmlResolver>(), CopyText());

        return await RunUnderCultureAsync(
            CultureInfo.GetCultureInfo("fi-FI"),
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
    }
}
