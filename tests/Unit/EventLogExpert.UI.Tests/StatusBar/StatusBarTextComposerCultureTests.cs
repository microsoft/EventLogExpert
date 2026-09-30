// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Localization;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Memory;
using EventLogExpert.Runtime.StatusBar;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.Localization;
using System.Collections.Immutable;
using System.Globalization;

namespace EventLogExpert.UI.Tests.StatusBar;

[Collection(CultureSensitiveCollection.Name)]
public sealed class StatusBarTextComposerCultureTests
{
    private readonly IStringLocalizer<SharedResource> _localizer = new MarkerLocalizer();

    [Fact]
    public void Loading_PercentageStaysUngroupedUnderCurrentCulture()
    {
        CultureInfo culture = CultureInfo.GetCultureInfo("fi-FI");

        string text = RunUnderCulture(culture, () =>
            StatusBarTextComposer.Loading(
                _localizer,
                ImmutableDictionary<StatusActivityId, LoadingProgress>.Empty.Add(
                    StatusActivityId.Create(),
                    new LoadingProgress(0, 50000, 100000)))!.Value.Text);

        Assert.Equal("[[StatusBar_Loading_PendingPercent(50)]]", text);
        Assert.DoesNotContain(culture.NumberFormat.NumberGroupSeparator, text, StringComparison.Ordinal);
    }

    [Fact]
    public void MemoryTooltip_GroupsByteWorkingSetUnderCurrentCulture()
    {
        CultureInfo culture = CultureInfo.GetCultureInfo("fi-FI");
        string expected = 1023.ToString("N0", culture);

        string text = RunUnderCulture(culture, () => StatusBarTextComposer.MemoryTooltip(_localizer, 1, 1023, MemoryUsageLevel.Normal));

        Assert.Contains(expected + " B", text, StringComparison.Ordinal);
        Assert.Contains(culture.NumberFormat.NumberGroupSeparator, text, StringComparison.Ordinal);
    }

    [Fact]
    public void MemoryTooltip_GroupsKibibyteWorkingSetUnderCurrentCulture()
    {
        CultureInfo culture = CultureInfo.GetCultureInfo("fi-FI");
        string expected = 1023.ToString("N0", culture);

        string text = RunUnderCulture(culture, () => StatusBarTextComposer.MemoryTooltip(_localizer, 1, 1023L * 1024, MemoryUsageLevel.Normal));

        Assert.Contains(expected + " KB", text, StringComparison.Ordinal);
        Assert.Contains(culture.NumberFormat.NumberGroupSeparator, text, StringComparison.Ordinal);
    }

    [Fact]
    public void MemoryValue_GroupsLargeGibibyteValuesUnderCurrentCulture()
    {
        CultureInfo culture = CultureInfo.GetCultureInfo("fi-FI");
        string expected = 1234.0.ToString("N1", culture);

        string text = RunUnderCulture(culture, () => StatusBarTextComposer.MemoryValue(_localizer, 1234 * 1024, MemoryUsageLevel.Normal));

        Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.Contains(culture.NumberFormat.NumberGroupSeparator, text, StringComparison.Ordinal);
        Assert.Contains(culture.NumberFormat.NumberDecimalSeparator, text, StringComparison.Ordinal);
    }

    [Fact]
    public void MemoryValue_GroupsLargeMebibyteValuesUnderCurrentCulture()
    {
        CultureInfo culture = CultureInfo.GetCultureInfo("fi-FI");
        string expected = 1000.ToString("N0", culture);

        string text = RunUnderCulture(culture, () => StatusBarTextComposer.MemoryValue(_localizer, 1000, MemoryUsageLevel.Normal));

        Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.Contains(culture.NumberFormat.NumberGroupSeparator, text, StringComparison.Ordinal);
    }

    [Fact]
    public void MemoryValue_UsesCurrentCultureDecimalSeparatorForGibibytes()
    {
        CultureInfo culture = CultureInfo.GetCultureInfo("fi-FI");

        string text = RunUnderCulture(culture, () => StatusBarTextComposer.MemoryValue(_localizer, 1536, MemoryUsageLevel.Normal));

        Assert.Contains(1.5.ToString("N1", culture), text, StringComparison.Ordinal);
        Assert.Contains(culture.NumberFormat.NumberDecimalSeparator, text, StringComparison.Ordinal);
    }

    [Fact]
    public void NewEventsLabel_GroupsDisplayCountUnderCurrentCulture()
    {
        CultureInfo culture = CultureInfo.GetCultureInfo("fi-FI");
        string expectedCount = 12345.ToString("N0", culture);

        string text = RunUnderCulture(culture, () => StatusBarTextComposer.NewEventsLabel(_localizer, 12345));

        Assert.Contains(expectedCount, text, StringComparison.Ordinal);
        Assert.Contains(culture.NumberFormat.NumberGroupSeparator, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Source_AllLogs_GroupsOpenLogCountUnderCurrentCulture()
    {
        CultureInfo culture = CultureInfo.GetCultureInfo("fi-FI");
        LogView allLogs = new(EventLogId.Create()) { GroupId = LogTabGroupId.AllLogs };
        LogView[] eventTables = Enumerable.Range(0, 12345)
            .Select(_ => new LogView(EventLogId.Create()))
            .Append(allLogs)
            .ToArray();

        string text = RunUnderCulture(culture, () => StatusBarTextComposer.Source(_localizer, allLogs, eventTables, []));

        Assert.Contains(12345.ToString("N0", culture), text, StringComparison.Ordinal);
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
