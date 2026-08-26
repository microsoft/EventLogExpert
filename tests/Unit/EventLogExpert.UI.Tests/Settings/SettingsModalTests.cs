// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Localization;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.DetailsPane;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.Modal;
using EventLogExpert.UI.Settings;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
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

    [Fact]
    public void SettingsModal_EnumOptionKeys_AllResolveInResources()
    {
        var localizer = Services.GetRequiredService<IStringLocalizer<SharedResource>>();

        foreach (var value in Enum.GetValues<Theme>())
        {
            Assert.False(localizer[$"Settings_Theme_{value}"].ResourceNotFound, $"Missing Settings_Theme_{value}");
        }

        foreach (var value in Enum.GetValues<EventCopyFormat>())
        {
            Assert.False(localizer[$"Settings_CopyFormat_{value}"].ResourceNotFound, $"Missing Settings_CopyFormat_{value}");
        }

        foreach (var value in Enum.GetValues<LogLevel>())
        {
            Assert.False(localizer[$"Settings_LogLevel_{value}"].ResourceNotFound, $"Missing Settings_LogLevel_{value}");
        }

        Assert.Equal("Follow System", localizer[$"Settings_Theme_{Theme.System}"].Value);
    }

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

        _announcementService.Received(1).Announce("Settings saved");
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
        Assert.Contains("Pre-release Builds", footerExtra.TextContent);
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
    public async Task SettingsModal_ThemeDropdown_ClosedDisplayIsLocalized_AndUpdatesOnSelection()
    {
        _settings.Theme.Returns(Theme.Light);
        var component = Render<SettingsModal>();

        Assert.Equal("Light", component.Find("#settings-theme").GetAttribute("value"));

        // ValueSelectItem wires @onmousedown (not onclick), so Click() would be a no-op here.
        var systemOption = component.FindAll("[role='option']")
            .Single(option => option.TextContent.Trim() == "Follow System");
        await systemOption.MouseDownAsync(new MouseEventArgs());

        Assert.Equal("Follow System", component.Find("#settings-theme").GetAttribute("value"));
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
    public void SettingsModal_TimeZoneInput_HasLabelForAssociation()
    {
        var component = Render<SettingsModal>();

        var label = component.Find("label[for='settings-timezone']");
        Assert.Equal("Time Zone:", label.TextContent.Trim());
        var input = component.Find("#settings-timezone");
        Assert.Equal("settings-timezone", input.Id);
    }

    [Fact]
    public void SettingsModal_ToggleRows_UseBareAriaLabels_ButColonInVisibleText()
    {
        var component = Render<SettingsModal>();

        Assert.Contains("Expand Display Pane On Selection Change:", component.Markup);
        Assert.Single(component.FindAll("[aria-label='Expand Display Pane On Selection Change']"));

        Assert.Contains("Pre-release Builds:", component.Markup);
        Assert.Single(component.FindAll("[aria-label='Pre-release Builds']"));
    }

    [Fact]
    public void SettingsModal_UsesAriaLabelNotTitleForUtilityModalConvention()
    {
        var component = Render<SettingsModal>();

        var dialog = component.Find("dialog");
        Assert.Equal("Settings", dialog.GetAttribute("aria-label"));
        Assert.False(dialog.HasAttribute("aria-labelledby"));
    }

    [Fact]
    public void SettingsModal_VerboseResolutionToggle_HasLabelForAssociation()
    {
        var component = Render<SettingsModal>();

        var label = component.Find("label[for='settings-verbose-resolution']");
        Assert.Equal("Verbose Event Resolution Logging:", label.TextContent.Trim());
        var input = component.Find("#settings-verbose-resolution");
        Assert.Equal("settings-verbose-resolution", input.Id);
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
