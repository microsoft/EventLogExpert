// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.UI.Banner;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Banner;

public sealed class InfoBannerTests : BunitContext
{
    private readonly IInfoBannerService _infoBannerService = Substitute.For<IInfoBannerService>();

    public InfoBannerTests()
    {
        Services.AddSingleton(_infoBannerService);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task InfoBanner_DismissClicked_CallsDismissInfoBannerWithEntryId()
    {
        var info = new BannerInfoEntry(BannerId.Create(), "Notice", "Heads up", BannerSeverity.Info, DateTime.UtcNow);

        var component = Render<InfoBanner>(p => p.Add(c => c.Entry, info));
        await component.Find("aside.banner-info button.banner-dismiss").ClickAsync(new MouseEventArgs());

        _infoBannerService.Received(1).DismissInfoBanner(info.Id);
    }

    [Fact]
    public void InfoBanner_InfoSeverity_RendersInfoStyledBanner()
    {
        var info = new BannerInfoEntry(BannerId.Create(), "Notice", "Heads up", BannerSeverity.Info, DateTime.UtcNow);

        var component = Render<InfoBanner>(p => p.Add(c => c.Entry, info));

        Assert.Single(component.FindAll("aside.banner.banner-info"));
        Assert.Empty(component.FindAll("aside.banner.banner-warning"));
        Assert.Contains("Notice: Heads up", component.Find("aside.banner-info").TextContent);
    }

    [Fact]
    public void InfoBanner_WarningSeverity_RendersWarningStyledBanner()
    {
        var info = new BannerInfoEntry(BannerId.Create(),
            "Slow",
            "Performance dip",
            BannerSeverity.Warning,
            DateTime.UtcNow);

        var component = Render<InfoBanner>(p => p.Add(c => c.Entry, info));

        Assert.Single(component.FindAll("aside.banner.banner-warning"));
        Assert.Empty(component.FindAll("aside.banner.banner-info"));
    }
}
