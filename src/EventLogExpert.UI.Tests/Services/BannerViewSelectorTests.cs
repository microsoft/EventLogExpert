// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;

namespace EventLogExpert.UI.Tests.Services;

public sealed class BannerViewSelectorTests
{
    private static readonly DateTime s_testTime = new(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Select_AllEmpty_ReturnsNone()
    {
        BannerView result = BannerViewSelector.Select(null, [], []);

        Assert.Equal(BannerView.None, result);
    }

    [Fact]
    public void Select_AllThree_CriticalWins()
    {
        BannerView result = BannerViewSelector.Select(
            new InvalidOperationException("boom"),
            [BuildError()],
            [BuildInfo()]);

        Assert.Equal(BannerView.Critical, result);
    }

    [Fact]
    public void Select_CriticalAndError_CriticalWins()
    {
        BannerView result = BannerViewSelector.Select(new InvalidOperationException("boom"), [BuildError()], []);

        Assert.Equal(BannerView.Critical, result);
    }

    [Fact]
    public void Select_CriticalAndInfo_CriticalWins()
    {
        BannerView result = BannerViewSelector.Select(new InvalidOperationException("boom"), [], [BuildInfo()]);

        Assert.Equal(BannerView.Critical, result);
    }

    [Fact]
    public void Select_ErrorAndInfo_ErrorWins()
    {
        BannerView result = BannerViewSelector.Select(null, [BuildError()], [BuildInfo()]);

        Assert.Equal(BannerView.Error, result);
    }

    [Fact]
    public void Select_OnlyCritical_ReturnsCritical()
    {
        BannerView result = BannerViewSelector.Select(new InvalidOperationException("boom"), [], []);

        Assert.Equal(BannerView.Critical, result);
    }

    [Fact]
    public void Select_OnlyError_ReturnsError()
    {
        BannerView result = BannerViewSelector.Select(null, [BuildError()], []);

        Assert.Equal(BannerView.Error, result);
    }

    [Fact]
    public void Select_OnlyInfo_ReturnsInfo()
    {
        BannerView result = BannerViewSelector.Select(null, [], [BuildInfo()]);

        Assert.Equal(BannerView.Info, result);
    }

    private static ErrorBannerEntry BuildError() =>
        new(Guid.NewGuid(), "Error Title", "Error Message", s_testTime);

    private static BannerInfoEntry BuildInfo() =>
        new(Guid.NewGuid(), "Info Title", "Info Message", BannerSeverity.Info, s_testTime);
}
