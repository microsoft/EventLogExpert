// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.UI.Common;
using Microsoft.Extensions.Localization;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Common;

public sealed class FilterLensLabelFormatterTests
{
    [Fact]
    public void Format_TimeRange_EvaluatesTheDayBoundaryInTheDisplayZone()
    {
        var localizer = Substitute.For<IStringLocalizer<SharedResource>>();
        var label = new FilterLensLabel.TimeRange(
            new DateTime(2024, 6, 1, 22, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 6, 2, 2, 0, 0, DateTimeKind.Utc));
        var eastOfUtc = TimeZoneInfo.CreateCustomTimeZone("H+05", TimeSpan.FromHours(5), "H+05", "H+05");

        var spansTwoDaysInUtc = FilterLensLabelFormatter.Format(localizer, label, TimeZoneInfo.Utc);
        var sameDayInDisplayZone = FilterLensLabelFormatter.Format(localizer, label, eastOfUtc);

        Assert.Contains($"{new DateTime(2024, 6, 1):d}", spansTwoDaysInUtc);
        Assert.Equal($"{new DateTime(2024, 6, 2, 3, 0, 0):T} - {new DateTime(2024, 6, 2, 7, 0, 0):T}", sameDayInDisplayZone);
    }
}
