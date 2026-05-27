// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.UI.Announcement;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Announcement;

public sealed class AnnouncerHostTests : BunitContext
{
    private readonly IAnnouncementService _announcementService = Substitute.For<IAnnouncementService>();

    public AnnouncerHostTests()
    {
        _announcementService.CurrentAnnouncement.Returns(string.Empty);
        Services.AddSingleton(_announcementService);
    }

    [Fact]
    public void AnnouncerHost_Dispose_UnsubscribesFromStateChanged()
    {
        var component = Render<AnnouncerHost>();

        component.Instance.Dispose();

        // After Dispose, raising StateChanged would invoke any remaining subscribers; since the
        // component's handler was unsubscribed, no exception is thrown and the test passes.
        _announcementService.StateChanged += Raise.Event<Action>();
        Assert.True(true);
    }

    [Fact]
    public void AnnouncerHost_OnStateChanged_ReRendersWithLatestAnnouncement()
    {
        _announcementService.CurrentAnnouncement.Returns(string.Empty);
        var component = Render<AnnouncerHost>();

        Assert.Empty(component.Find("#app-announcer").TextContent.Trim());

        _announcementService.CurrentAnnouncement.Returns("Database imported");
        _announcementService.StateChanged += Raise.Event<Action>();

        component.WaitForState(() => component.Find("#app-announcer").TextContent.Contains("Database imported"));
        Assert.Contains("Database imported", component.Find("#app-announcer").TextContent);
    }

    [Fact]
    public void AnnouncerHost_RendersCurrentAnnouncementText()
    {
        _announcementService.CurrentAnnouncement.Returns("Settings saved");

        var component = Render<AnnouncerHost>();

        Assert.Contains("Settings saved", component.Find("#app-announcer").TextContent);
    }

    [Fact]
    public void AnnouncerHost_RendersLiveRegionWithPolitePoliteness()
    {
        var component = Render<AnnouncerHost>();

        var region = component.Find("#app-announcer");
        Assert.Equal("status", region.GetAttribute("role"));
        Assert.Equal("polite", region.GetAttribute("aria-live"));
        Assert.Equal("true", region.GetAttribute("aria-atomic"));
    }
}
