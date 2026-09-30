// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.Runtime.Tests.TestUtils;
using System.Globalization;

namespace EventLogExpert.Runtime.Tests.Histogram;

[Collection(CultureSensitiveCollection.Name)]
public sealed class HistogramBuilderCultureTests
{
    private static readonly EventLogId s_logId = EventLogId.Create();

    [Fact]
    public void Build_GroupByEventId_DoesNotGroupLargeValuesUnderCurrentCulture()
    {
        CultureInfo culture = CultureInfo.GetCultureInfo("fi-FI");
        var view = DisplayViewTestFactory.Build(s_logId, IdEvents((1234567, 2)));

        HistogramData? data = RunUnderCulture(culture, () =>
            HistogramBuilder.Build(view, HistogramDimension.EventId, maxBuckets: 100, CancellationToken.None));

        Assert.NotNull(data);
        data.Groups[0].AssertDataValue("1234567");
        var label = Assert.IsType<HistogramGroupLabel.DataValue>(data.Groups[0].Label);
        Assert.DoesNotContain(culture.NumberFormat.NumberGroupSeparator, label.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_GroupByEventId_LabelsUseInvariantNegativeSignUnderCustomCulture()
    {
        CultureInfo culture = CultureInfo.GetCultureInfo("fi-FI").Clone() as CultureInfo ?? throw new InvalidOperationException("Expected cloneable culture.");
        culture.NumberFormat.NegativeSign = "[MINUS]";
        var view = DisplayViewTestFactory.Build(s_logId, IdEvents((-1234567, 2)));

        HistogramData? data = RunUnderCulture(culture, () =>
            HistogramBuilder.Build(view, HistogramDimension.EventId, maxBuckets: 100, CancellationToken.None));

        Assert.NotNull(data);
        data.Groups[0].AssertDataValue("-1234567");
        var label = Assert.IsType<HistogramGroupLabel.DataValue>(data.Groups[0].Label);
        Assert.Contains("-1234567", label.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(culture.NumberFormat.NegativeSign, label.Text, StringComparison.Ordinal);
    }

    private static ResolvedEvent[] IdEvents(params (int Id, int Count)[] groups)
    {
        var events = new List<ResolvedEvent>();
        long ticks = 0;

        foreach ((int id, int count) in groups)
        {
            for (int index = 0; index < count; index++)
            {
                events.Add(new ResolvedEvent("TestLog", LogPathType.Channel)
                {
                    Id = id,
                    TimeCreated = new DateTime(ticks++, DateTimeKind.Utc)
                });
            }
        }

        return [.. events];
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
}
