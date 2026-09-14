// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Common;
using EventLogExpert.UI.Tests.TestUtils;

namespace EventLogExpert.UI.Tests.Common;

public sealed class LocalizedCountTests
{
    private readonly MarkerLocalizer _localizer = new();

    [Fact]
    public void OneOrManyRaw_WithExtraArgs_AllowsCountAtSecondPositionWithoutGrouping() =>
        Assert.Equal(
            "[[Count_Many(alpha|1000|omega)]]",
            LocalizedCount.OneOrManyRaw(_localizer, 1000, "Count_One", "Count_Many", "alpha", 1000, "omega"));

    [Fact]
    public void OneOrManyRaw_WithExtraArgs_AllowsCountAtThirdPositionWithoutGrouping() =>
        Assert.Equal(
            "[[Count_Many(alpha|omega|1000)]]",
            LocalizedCount.OneOrManyRaw(_localizer, 1000, "Count_One", "Count_Many", "alpha", "omega", 1000));

    [Theory]
    [InlineData(1, "[[Count_One(1|alpha|omega)]]")]
    [InlineData(2, "[[Count_Many(2|alpha|omega)]]")]
    public void OneOrManyRaw_WithExtraArgs_UsesSelectionCountAndCallerArgumentOrder(int count, string expected) =>
        Assert.Equal(expected, LocalizedCount.OneOrManyRaw(_localizer, count, "Count_One", "Count_Many", count, "alpha", "omega"));

    [Theory]
    [InlineData(1, 1, "[[ItemOne_DupOne(1|1)]]")]
    [InlineData(1, 2, "[[ItemOne_DupMany(1|2)]]")]
    [InlineData(2, 1, "[[ItemMany_DupOne(2|1)]]")]
    [InlineData(2, 2, "[[ItemMany_DupMany(2|2)]]")]
    public void OneOrManyRaw_WithTwoCounts_SelectsAllFourCombinations(int itemCount, int duplicateCount, string expected) =>
        Assert.Equal(
            expected,
            LocalizedCount.OneOrManyRaw(
                _localizer,
                itemCount,
                duplicateCount,
                "ItemOne_DupOne",
                "ItemOne_DupMany",
                "ItemMany_DupOne",
                "ItemMany_DupMany",
                itemCount,
                duplicateCount));
}
