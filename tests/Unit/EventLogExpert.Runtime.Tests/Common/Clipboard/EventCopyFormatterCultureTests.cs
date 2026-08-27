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

/// <summary>
///     Pins the log-table copy path (<see cref="EventCopyFormatter" />, the highest-volume copy surface) as
///     regional-culture-independent for its structural English: the Full-format <c>"Date: "</c> label and the Markdown
///     <c>"Description"</c> header stay present under a foreign <see cref="CultureInfo.CurrentCulture" />. The Date VALUE
///     is CurrentCulture-formatted by design (documented, deferred - <c>loc-copyexport-value-invariance</c>); only the
///     label is pinned. UICulture localizer-independence is deferred to the copy-localization increment. Contrast culture
///     fi-FI per <c>EventTableExporterCultureTests</c>.
/// </summary>
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
    public async Task FormatAsync_FullFormat_EmitsEnglishDateLabel_UnderForeignCulture() =>
        Assert.Contains("Date: ", await FormatUnderContrastCultureAsync(EventCopyFormat.Full), StringComparison.Ordinal);

    [Fact]
    public async Task FormatAsync_MarkdownFormat_EmitsEnglishHeaderRow_UnderForeignCulture()
    {
        string result = await FormatUnderContrastCultureAsync(EventCopyFormat.Markdown);

        // Markdown-specific: the pipe-table header with the hardcoded English "Description" column (EventCopyFormatter.cs:166).
        // Asserting the full "| ... | Description |" row (not a bare "Description") ensures this cannot pass by matching the
        // Full/line format, which emits no pipe table.
        Assert.StartsWith("|", result, StringComparison.Ordinal);
        Assert.Contains("| Level | Source | Event ID | Description |", result, StringComparison.Ordinal);
    }

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

        var formatter = new EventCopyFormatter(detailResolver, Substitute.For<IEventXmlResolver>());

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
}
