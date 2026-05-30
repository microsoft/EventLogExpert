// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.DetailsPane;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.Settings;
using EventLogExpert.UI.Tests.TestUtils;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Settings;

public sealed class SettingsModalTests : BunitContext
{
    private readonly IAnnouncementService _announcementService = Substitute.For<IAnnouncementService>();
    private readonly IDetailsPanePreferencesProvider _detailsPanePreferences = Substitute.For<IDetailsPanePreferencesProvider>();
    private readonly IModalCoordinator _modalCoordinator = Substitute.For<IModalCoordinator>();
    private readonly IModalService _modalService = Substitute.For<IModalService>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();

    public SettingsModalTests()
    {
        Services.AddBannerHostDependencies();
        Services.AddMenuMocks();

        _modalService.ActiveModalId.Returns(new ModalId(1L));

        _settings.TimeZoneId.Returns(string.Empty);

        Services.AddSingleton(_announcementService);
        Services.AddSingleton(_detailsPanePreferences);
        Services.AddSingleton(_modalCoordinator);
        Services.AddSingleton(_modalService);
        Services.AddSingleton(_settings);
        Services.AddFluxor(options => options.ScanAssemblies(typeof(SettingsModal).Assembly));

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task SettingsModal_OnSaveSuccess_AnnouncesSettingsSaved()
    {
        var component = Render<SettingsModal>();
        // Internal test-only forwarder bypasses ModalChrome footer markup coupling.
        await component.InvokeAsync(() => component.Instance.InvokeOnSaveAsyncForTests());

        _announcementService.Received(1).Announce("Settings saved");
    }

    [Fact]
    public void SettingsModal_PreReleaseBuilds_LivesInFooterNotBody()
    {
        var component = Render<SettingsModal>();

        var footerExtra = component.Find(".footer-extra");
        Assert.Contains("Pre-release Builds", footerExtra.TextContent);
    }

    [Fact]
    public void SettingsModal_TimeZoneInput_HasLabelForAssociation()
    {
        var component = Render<SettingsModal>();

        var label = component.Find("label[for='settings-timezone']");
        Assert.Equal("Time Zone:", label.TextContent.Trim());
        var input = component.Find("#settings-timezone");
        Assert.Equal("settings-timezone", input.Id);
    }

    [Fact]
    public void SettingsModal_UsesAriaLabelNotTitleForUtilityModalConvention()
    {
        var component = Render<SettingsModal>();

        var dialog = component.Find("dialog");
        Assert.Equal("Settings", dialog.GetAttribute("aria-label"));
        Assert.False(dialog.HasAttribute("aria-labelledby"));
    }
}
