// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Localization;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.UI.DatabaseTools;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Collections.Immutable;
using System.Globalization;

namespace EventLogExpert.UI.Tests.DatabaseTools;

[Collection(CultureSensitiveCollection.Name)]
public sealed class DatabaseToolsLogViewCultureTests : BunitContext
{
    private readonly IClipboardService _clipboard = Substitute.For<IClipboardService>();

    public DatabaseToolsLogViewCultureTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(Substitute.For<IAlertDialogService>());
        Services.AddSingleton(_clipboard);
        Services.AddSingleton(Substitute.For<IFileSaveService>());
        Services.AddSingleton<IStringLocalizer<SharedResource>>(new MarkerLocalizer());
        Services.AddSingleton(Substitute.For<ITraceLogger>());
        _clipboard.CopyTextAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task LogTimestampAndCopiedEntry_StayFixedInvariantAcrossCultures()
    {
        var timestamp = new DateTime(2026, 8, 26, 17, 57, 5, 123, DateTimeKind.Utc);
        ImmutableList<LogRecord> entries = [new LogRecord(timestamp, LogLevel.Information, "ready")];

        string englishTimestamp = RenderTimestamp(CultureInfo.GetCultureInfo("en-US"), entries);
        string arabicTimestamp = RenderTimestamp(CultureInfo.GetCultureInfo("ar-SA"), entries);
        await CopyUnderCultureAsync(CultureInfo.GetCultureInfo("ar-SA"), entries);

        Assert.Equal("17:57:05.123", englishTimestamp);
        Assert.Equal(englishTimestamp, arabicTimestamp);
        await _clipboard.Received(1).CopyTextAsync("[17:57:05.123] [Information] ready");
    }

    private static T RunUnderCulture<T>(CultureInfo culture, Func<T> build)
    {
        CultureInfo priorCulture = CultureInfo.CurrentCulture;
        CultureInfo priorUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            return build();
        }
        finally
        {
            CultureInfo.CurrentCulture = priorCulture;
            CultureInfo.CurrentUICulture = priorUiCulture;
        }
    }

    private static async Task RunUnderCultureAsync(CultureInfo culture, Func<Task> build)
    {
        CultureInfo priorCulture = CultureInfo.CurrentCulture;
        CultureInfo priorUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            await build();
        }
        finally
        {
            CultureInfo.CurrentCulture = priorCulture;
            CultureInfo.CurrentUICulture = priorUiCulture;
        }
    }

    private async Task CopyUnderCultureAsync(CultureInfo culture, ImmutableList<LogRecord> entries)
    {
        await RunUnderCultureAsync(culture, async () =>
        {
            IRenderedComponent<DatabaseToolsLogView> component = Render<DatabaseToolsLogView>(parameters => parameters
                .Add(view => view.Entries, entries));
            await component.FindAll("button").First(button => button.TextContent.Contains("[[DatabaseTools_Log_Copy]]", StringComparison.Ordinal)).ClickAsync(new MouseEventArgs());
        });
    }

    private string RenderTimestamp(CultureInfo culture, ImmutableList<LogRecord> entries)
    {
        IRenderedComponent<DatabaseToolsLogView> component = RunUnderCulture(culture, () => Render<DatabaseToolsLogView>(parameters => parameters
            .Add(view => view.Entries, entries)));
        return component.Find(".log-timestamp").TextContent;
    }
}
