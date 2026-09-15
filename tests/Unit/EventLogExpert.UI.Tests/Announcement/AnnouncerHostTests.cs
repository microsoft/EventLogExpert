// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Localization;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.FilterLibrary;
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
    public void AnnouncerHost_FilterImportCompleted_RoutesThroughImportComposer()
    {
        _announcementService.Current.Returns(new CurrentAnnouncement(
            new AnnouncementPayload.FilterImportCompleted(new ImportSummary(2, 1, 1, 3, 0)),
            2));

        var component = Render<AnnouncerHost>();

        Assert.Equal(
            "[[FilterImport_Summary_TagOne(2|1|1|3)]]",
            component.Find("#app-announcer").TextContent);
    }

    [Fact]
    public void AnnouncerHost_LensGroupSaved_RoutesThroughLocalizer()
    {
        // The structured LensGroupSaved payload must have a switch arm (the default arm throws), and it localizes
        // the "saved as group" wording with the group name at render time.
        _announcementService.Current.Returns(new CurrentAnnouncement(new AnnouncementPayload.LensGroupSaved("My Group"), 1));

        var component = Render<AnnouncerHost>();

        var text = component.Find("#app-announcer").TextContent;
        Assert.Contains("[[FilterLens_SavedAsGroupAnnouncement(", text);
        Assert.Contains("My Group", text);
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

        component.WaitForAssertion(() =>
        {
            var second = component.Find("#app-announcer").TextContent;
            Assert.NotEqual(first, second);
            Assert.Contains("[[FilterLens_KeptAnnouncement(", second);
        });
    }

    [Fact]
    public void AnnouncerHost_LensesSavedAll_RoutesThroughLocalizer()
    {
        // The structured LensesSavedAll payload must have a switch arm (the default arm throws) and localizes the
        // "saved all" wording.
        _announcementService.Current.Returns(new CurrentAnnouncement(new AnnouncementPayload.LensesSavedAll(), 1));

        var component = Render<AnnouncerHost>();

        Assert.Contains("[[FilterLens_SavedAllAnnouncement", component.Find("#app-announcer").TextContent);
    }

    [Fact]
    public void AnnouncerHost_OnStateChanged_ReRendersWithLatestAnnouncement()
    {
        _announcementService.Current.Returns(new CurrentAnnouncement(new AnnouncementPayload.Text(string.Empty), 0));
        var component = Render<AnnouncerHost>();

        Assert.Empty(component.Find("#app-announcer").TextContent.Trim());

        _announcementService.Current.Returns(new CurrentAnnouncement(new AnnouncementPayload.Text("Database imported"), 2));
        _announcementService.StateChanged += Raise.Event<Action>();

        component.WaitForAssertion(() =>
            Assert.Contains("Database imported", component.Find("#app-announcer").TextContent));
    }

    [Fact]
    public void AnnouncerHost_RendersCurrentAnnouncementText()
    {
        _announcementService.Current.Returns(new CurrentAnnouncement(new AnnouncementPayload.Text("Settings saved"), 2));

        var component = Render<AnnouncerHost>();

        Assert.Contains("Settings saved", component.Find("#app-announcer").TextContent);
    }

    [Fact]
    public void AnnouncerHost_RendersEveryAnnouncementLeafWithoutThrowing()
    {
        foreach (var leafType in typeof(AnnouncementPayload).GetNestedTypes().Where(type => !type.IsAbstract))
        {
            _announcementService.Current.Returns(new CurrentAnnouncement(CreateAnnouncementPayload(leafType), 2));

            var component = Render<AnnouncerHost>();

            Assert.NotNull(component.Find("#app-announcer").TextContent);
        }
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
    public void AnnouncerHost_TagRemoved_RoutesThroughImportComposer()
    {
        _announcementService.Current.Returns(new CurrentAnnouncement(new AnnouncementPayload.TagRemoved("bug", 2), 2));

        var component = Render<AnnouncerHost>();

        Assert.Equal(
            "[[FilterImport_Announcement_TagRemoved_Many(bug|2)]]",
            component.Find("#app-announcer").TextContent);
    }

    [Fact]
    public void AnnouncerHost_TagRenamed_RoutesThroughImportComposer()
    {
        _announcementService.Current.Returns(new CurrentAnnouncement(new AnnouncementPayload.TagRenamed("bug", "defect", 1), 2));

        var component = Render<AnnouncerHost>();

        Assert.Equal(
            "[[FilterImport_Announcement_TagRenamed_One(bug|defect|1)]]",
            component.Find("#app-announcer").TextContent);
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

        component.WaitForAssertion(() =>
        {
            var second = component.Find("#app-announcer").TextContent;
            Assert.NotEqual(first, second);
            Assert.Contains("Database imported", second);
        });

        Assert.Contains("Database imported", first);
    }

    private static AnnouncementPayload CreateAnnouncementPayload(Type leafType) =>
        leafType == typeof(AnnouncementPayload.Text) ? new AnnouncementPayload.Text("message") :
        leafType == typeof(AnnouncementPayload.LensKept) ? new AnnouncementPayload.LensKept(
            new FilterLensLabel.PropertyComparison(EventProperty.ActivityId, IsEqual: true, "abc")) :
        leafType == typeof(AnnouncementPayload.LensGroupSaved) ? new AnnouncementPayload.LensGroupSaved("group") :
        leafType == typeof(AnnouncementPayload.LensesSavedAll) ? new AnnouncementPayload.LensesSavedAll() :
        leafType == typeof(AnnouncementPayload.FilterImportCompleted) ? new AnnouncementPayload.FilterImportCompleted(new ImportSummary(1, 2, 3, 4, 5)) :
        leafType == typeof(AnnouncementPayload.TagRemoved) ? new AnnouncementPayload.TagRemoved("tag", 2) :
        leafType == typeof(AnnouncementPayload.TagRenamed) ? new AnnouncementPayload.TagRenamed("old", "new", 2) :
        throw new InvalidOperationException($"No test fixture for {leafType.FullName}.");
}
