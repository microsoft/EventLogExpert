// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Localization;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.DetailsPane;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.Modal;
using EventLogExpert.UI.Settings;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
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

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private IStringLocalizer<SharedResource> Localizer =>
        Services.GetRequiredService<IStringLocalizer<SharedResource>>();

    [Fact]
    public void SettingsModal_LoadsVerboseResolutionFromSettings()
    {
        _settings.VerboseResolution.Returns(true);

        var component = Render<SettingsModal>();

        Assert.True(component.Find("#settings-verbose-resolution").HasAttribute("checked"));
    }

    [Fact]
    public async Task SettingsModal_OnSaveSuccess_AnnouncesSettingsSaved()
    {
        var component = Render<SettingsModal>();
        await component.InvokeAsync(() => component.Instance.InvokeOnSaveAsyncForTests());

        _announcementService.Received(1).Announce(Localizer["Settings_SavedAnnouncement"].Value);
    }

    [Fact]
    public async Task SettingsModal_OnSave_PersistsStagedVerboseResolution()
    {
        _settings.VerboseResolution.Returns(false);
        var component = Render<SettingsModal>();
        component.Find("#settings-verbose-resolution").Change(true);

        await component.InvokeAsync(() => component.Instance.InvokeOnSaveAsyncForTests());

        _settings.Received().VerboseResolution = true;
    }

    [Fact]
    public void SettingsModal_PreReleaseBuilds_LivesInFooterNotBody()
    {
        var component = Render<SettingsModal>();

        var footerExtra = component.Find(".footer-extra");
        Assert.Contains(Localizer["Settings_PreReleaseLabel"].Value, footerExtra.TextContent);
    }

    [Fact]
    public void SettingsModal_ResolvesAllResourceKeys_NoRawKeyLeaksIntoMarkup()
    {
        // A missing key makes IStringLocalizer echo the key name; ids/classes use hyphens (settings-timezone, modal-title),
        // so a raw "Settings_" or "Modal_" in the markup is an unresolved-key leak on any label, aria, or dropdown option.
        var component = Render<SettingsModal>();

        Assert.DoesNotContain("Settings_", component.Markup);
        Assert.DoesNotContain("Modal_", component.Markup);
    }

    [Fact]
    public void SettingsModal_TimeZoneClosedDisplay_EmptyIdRendersEmpty()
    {
        _settings.TimeZoneId.Returns(string.Empty);

        var component = Render<SettingsModal>();

        Assert.Equal(string.Empty, component.Find("#settings-timezone").GetAttribute("value"));
    }

    [Fact]
    public void SettingsModal_TimeZoneClosedDisplay_FallsBackToRawIdForUnknownId()
    {
        _settings.TimeZoneId.Returns("Not A Real Time Zone Id");

        var component = Render<SettingsModal>();

        Assert.Equal("Not A Real Time Zone Id", component.Find("#settings-timezone").GetAttribute("value"));
    }

    [Fact]
    public void SettingsModal_TimeZoneClosedDisplay_ShowsDisplayNameForKnownId()
    {
        var zone = TimeZoneInfo.GetSystemTimeZones()[0];
        _settings.TimeZoneId.Returns(zone.Id);

        var component = Render<SettingsModal>();

        Assert.Equal(zone.DisplayName, component.Find("#settings-timezone").GetAttribute("value"));
    }

    [Fact]
    public void SettingsModal_UsesAriaLabelNotTitleForUtilityModalConvention()
    {
        var component = Render<SettingsModal>();
        var dialog = component.Find("dialog");

        Assert.False(dialog.HasAttribute("aria-labelledby"));
    }

    [Fact]
    public void SettingsModal_WhenNotSaved_DoesNotPersistVerboseResolution()
    {
        _settings.VerboseResolution.Returns(false);
        var component = Render<SettingsModal>();

        component.Find("#settings-verbose-resolution").Change(true);

        _settings.DidNotReceive().VerboseResolution = Arg.Any<bool>();
    }
}
