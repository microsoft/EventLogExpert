// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Globalization;

namespace EventLogExpert.UI.Tests.Localization;

[Collection(CultureSensitiveCollection.Name)]
public sealed class HistogramTextComposerCultureTests
{
    private readonly IStringLocalizer<SharedResource> _localizer = new ServiceCollection()
        .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
        .AddEventLogLocalization()
        .BuildServiceProvider()
        .GetRequiredService<IStringLocalizer<SharedResource>>();

    [Fact]
    public void BarTooltip_SameDayTimelineDatesStayFixedInvariantAcrossCultures()
    {
        var start = new DateTime(2026, 8, 26, 17, 57, 5, DateTimeKind.Unspecified);
        var end = new DateTime(2026, 8, 26, 18, 2, 3, DateTimeKind.Unspecified);

        string english = RunUnderCulture(CultureInfo.GetCultureInfo("en-US"), () =>
            HistogramTextComposer.BarTooltip(_localizer, 1, HistogramEventNoun.Events, start, end, windowCrossesDay: false, []));
        string arabic = RunUnderCulture(CultureInfo.GetCultureInfo("ar-SA"), () =>
            HistogramTextComposer.BarTooltip(_localizer, 1, HistogramEventNoun.Events, start, end, windowCrossesDay: false, []));

        Assert.Equal(english, arabic);
        Assert.Contains("17:57:05", english, StringComparison.Ordinal);
        Assert.Contains("18:02:03", english, StringComparison.Ordinal);
        Assert.DoesNotContain("2026-08-26", english, StringComparison.Ordinal);
    }

    [Fact]
    public void BarTooltip_TimelineDatesStayFixedInvariantAcrossCultures()
    {
        var start = new DateTime(2026, 8, 26, 17, 57, 5, DateTimeKind.Unspecified);
        var end = new DateTime(2026, 8, 27, 1, 2, 3, DateTimeKind.Unspecified);

        string english = RunUnderCulture(CultureInfo.GetCultureInfo("en-US"), () =>
            HistogramTextComposer.BarTooltip(_localizer, 1, HistogramEventNoun.Events, start, end, windowCrossesDay: true, []));
        string arabic = RunUnderCulture(CultureInfo.GetCultureInfo("ar-SA"), () =>
            HistogramTextComposer.BarTooltip(_localizer, 1, HistogramEventNoun.Events, start, end, windowCrossesDay: true, []));

        Assert.Equal(english, arabic);
        Assert.Contains("2026-08-26 17:57:05", english, StringComparison.Ordinal);
        Assert.Contains("2026-08-27 01:02:03", english, StringComparison.Ordinal);
    }

    [Fact]
    public void NarrationDatesUseCurrentCultureGeneralShortPattern()
    {
        var start = new DateTime(2026, 8, 26, 17, 57, 5, DateTimeKind.Unspecified);
        var end = new DateTime(2026, 8, 26, 18, 2, 3, DateTimeKind.Unspecified);
        CultureInfo englishCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo thaiCulture = CultureInfo.GetCultureInfo("th-TH");

        AssertCurrentCultureNarration(
            englishCulture,
            thaiCulture,
            start,
            end,
            () => HistogramTextComposer.BinCursorAnnouncement(_localizer, 1, HistogramEventNoun.Events, start, end, isSpike: false, []));
        AssertCurrentCultureNarration(
            englishCulture,
            thaiCulture,
            start,
            end,
            () => HistogramTextComposer.RegionAria(_localizer, 1, HistogramEventNoun.Events, start, end, []));
        AssertCurrentCultureNarration(
            englishCulture,
            thaiCulture,
            start,
            end,
            () => HistogramTextComposer.WindowAnnouncement(_localizer, 1, HistogramEventNoun.Events, start, end, []));
    }

    private static void AssertCurrentCultureNarration(
        CultureInfo englishCulture,
        CultureInfo thaiCulture,
        DateTime start,
        DateTime end,
        Func<string> render)
    {
        string english = RunUnderCulture(englishCulture, render);
        string thai = RunUnderCulture(thaiCulture, render);

        Assert.Contains(start.ToString("g", englishCulture), english, StringComparison.Ordinal);
        Assert.Contains(end.ToString("g", englishCulture), english, StringComparison.Ordinal);
        Assert.Contains(start.ToString("g", thaiCulture), thai, StringComparison.Ordinal);
        Assert.Contains(end.ToString("g", thaiCulture), thai, StringComparison.Ordinal);
        Assert.NotEqual(english, thai);
    }

    private static string RunUnderCulture(CultureInfo culture, Func<string> build)
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
}
