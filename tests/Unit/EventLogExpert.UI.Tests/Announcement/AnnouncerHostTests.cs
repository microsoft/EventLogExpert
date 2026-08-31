// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Localization;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.UI.Announcement;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;
using AnnouncementPayload = EventLogExpert.Runtime.Announcement.Announcement;

namespace EventLogExpert.UI.Tests.Announcement;

public sealed class AnnouncerHostTests : BunitContext
{
    private readonly IAnnouncementService _announcementService = Substitute.For<IAnnouncementService>();

    public AnnouncerHostTests()
    {
        _announcementService.Current.Returns(new CurrentAnnouncement(new AnnouncementPayload.Text(string.Empty), 0));
        Services.AddSingleton(_announcementService);
        Services.AddSingleton<IStringLocalizer<SharedResource>>(new MarkerLocalizer());
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
    public void AnnouncerHost_LensKeptReannounced_RoutesThroughLocalizerAndMutatesText()
    {
        // The structured LensKept payload localizes the "kept as filter" wording at render time (MarkerLocalizer echoes
        // the key plus the formatted label), and the odd/even sequence toggle still mutates the rendered text so an
        // identical re-kept lens re-announces.
        var label = new FilterLensLabel.PropertyComparison(EventProperty.ActivityId, IsEqual: true, "abc");
        _announcementService.Current.Returns(new CurrentAnnouncement(new AnnouncementPayload.LensKept(label), 1));
        var component = Render<AnnouncerHost>();
        var first = component.Find("#app-announcer").TextContent;

        Assert.Contains("[[FilterLens_KeptAnnouncement(", first);
        Assert.Contains("[[FilterLens_Property_ActivityId]] = abc", first);

        _announcementService.Current.Returns(new CurrentAnnouncement(new AnnouncementPayload.LensKept(label), 2));
        _announcementService.StateChanged += Raise.Event<Action>();

        component.WaitForState(() => component.Find("#app-announcer").TextContent != first);
        var second = component.Find("#app-announcer").TextContent;

        Assert.NotEqual(first, second);
        Assert.Contains("[[FilterLens_KeptAnnouncement(", second);
    }

    [Fact]
    public void AnnouncerHost_OnStateChanged_ReRendersWithLatestAnnouncement()
    {
        _announcementService.Current.Returns(new CurrentAnnouncement(new AnnouncementPayload.Text(string.Empty), 0));
        var component = Render<AnnouncerHost>();

        Assert.Empty(component.Find("#app-announcer").TextContent.Trim());

        _announcementService.Current.Returns(new CurrentAnnouncement(new AnnouncementPayload.Text("Database imported"), 2));
        _announcementService.StateChanged += Raise.Event<Action>();

        component.WaitForState(() => component.Find("#app-announcer").TextContent.Contains("Database imported"));
        Assert.Contains("Database imported", component.Find("#app-announcer").TextContent);
    }

    [Fact]
    public void AnnouncerHost_RendersCurrentAnnouncementText()
    {
        _announcementService.Current.Returns(new CurrentAnnouncement(new AnnouncementPayload.Text("Settings saved"), 2));

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

    [Fact]
    public void AnnouncerHost_TwoIdenticalMessages_RenderedTextMutatesForReannouncement()
    {
        // Relocated from AnnouncementServiceTests: SR live regions do not re-announce when the text node is unchanged.
        // The host (not the service) appends a zero-width space on odd sequences so two identical consecutive
        // announcements still mutate the rendered DOM text; NVDA/JAWS/VoiceOver do not pronounce the ZWSP.
        _announcementService.Current.Returns(new CurrentAnnouncement(new AnnouncementPayload.Text("Database imported"), 1));
        var component = Render<AnnouncerHost>();
        var first = component.Find("#app-announcer").TextContent;

        _announcementService.Current.Returns(new CurrentAnnouncement(new AnnouncementPayload.Text("Database imported"), 2));
        _announcementService.StateChanged += Raise.Event<Action>();

        component.WaitForState(() => component.Find("#app-announcer").TextContent != first);
        var second = component.Find("#app-announcer").TextContent;

        Assert.NotEqual(first, second);
        Assert.Contains("Database imported", first);
        Assert.Contains("Database imported", second);
    }
}
