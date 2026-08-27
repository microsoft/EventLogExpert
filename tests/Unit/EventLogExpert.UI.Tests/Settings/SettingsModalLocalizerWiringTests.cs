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

public sealed class SettingsModalLocalizerWiringTests : BunitContext
{
    private readonly IAnnouncementService _announcementService = Substitute.For<IAnnouncementService>();
    private readonly IDetailsPanePreferencesProvider _detailsPanePreferences = Substitute.For<IDetailsPanePreferencesProvider>();
    private readonly IModalCoordinator _modalCoordinator = Substitute.For<IModalCoordinator>();
    private readonly IModalService _modalService = Substitute.For<IModalService>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();

    public SettingsModalLocalizerWiringTests()
    {
        Services.AddBannerHostDependencies();
        Services.AddMenuMocks();

        _modalService.ActiveModalId.Returns(new ModalId(1L));
        _settings.CopyFormat.Returns(EventCopyFormat.Default);
        _settings.LogLevel.Returns(LogLevel.Information);
        _settings.Theme.Returns(Theme.System);
        _settings.TimeZoneId.Returns(string.Empty);

        Services.AddSingleton(_announcementService);
        Services.AddSingleton(_detailsPanePreferences);
        Services.AddSingleton(_modalCoordinator);
        Services.AddSingleton(_modalService);
        Services.AddSingleton(_settings);
        Services.AddSingleton<IStringLocalizer<SharedResource>>(new MarkerLocalizer());

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void LabelsAriaAndModalChromeText_AreDrivenByTheLocalizer()
    {
        var component = Render<SettingsModal>();

        Assert.Equal("[[Settings_AriaLabel]]", component.Find("dialog").GetAttribute("aria-label"));
        Assert.Equal("[[Settings_TimeZoneLabel]]", component.Find("label[for='settings-timezone']").TextContent.Trim());
        Assert.Equal("[[Settings_ThemeLabel]]", component.Find("label[for='settings-theme']").TextContent.Trim());
        Assert.Equal("[[Settings_KeyboardCopyLabel]]", component.Find("label[for='settings-copy-format']").TextContent.Trim());
        Assert.Equal("[[Settings_LoggingLevelLabel]]", component.Find("label[for='settings-log-level']").TextContent.Trim());
        Assert.Equal("[[Settings_VerboseResolutionLabel]]", component.Find("label[for='settings-verbose-resolution']").TextContent.Trim());
        Assert.Contains("[[Settings_ExpandDisplayPaneLabel]]", component.Markup);
        Assert.Contains("[[Settings_PreReleaseLabel]]", component.Markup);
        Assert.Equal("[[Modal_Save]]", component.FindAll(".footer-group button")[0].TextContent.Trim());
        Assert.Equal("[[Modal_Exit]]", component.FindAll(".footer-group button")[1].TextContent.Trim());
    }

    [Fact]
    public async Task ThemeDropdown_ClosedDisplayIsLocalized_AndUpdatesOnSelection()
    {
        _settings.Theme.Returns(Theme.Light);
        var component = Render<SettingsModal>();

        Assert.Equal("[[Settings_Theme_Light]]", component.Find("#settings-theme").GetAttribute("value"));

        var systemOption = component.FindAll("[role='option']")
            .Single(option => option.TextContent.Trim() == "[[Settings_Theme_System]]");
        await systemOption.MouseDownAsync(new MouseEventArgs());

        Assert.Equal("[[Settings_Theme_System]]", component.Find("#settings-theme").GetAttribute("value"));
    }

    [Fact]
    public void TimeZoneAndVerboseRows_KeepLabelAssociations()
    {
        var component = Render<SettingsModal>();

        var timeZoneLabel = component.Find("label[for='settings-timezone']");
        Assert.Equal("[[Settings_TimeZoneLabel]]", timeZoneLabel.TextContent.Trim());
        Assert.Equal("settings-timezone", component.Find("#settings-timezone").Id);

        var verboseLabel = component.Find("label[for='settings-verbose-resolution']");
        Assert.Equal("[[Settings_VerboseResolutionLabel]]", verboseLabel.TextContent.Trim());
        Assert.Equal("settings-verbose-resolution", component.Find("#settings-verbose-resolution").Id);
    }

    [Fact]
    public void ToggleRows_UseDistinctVisibleAndAriaKeys()
    {
        var component = Render<SettingsModal>();

        Assert.Contains("[[Settings_ExpandDisplayPaneLabel]]", component.Markup);
        Assert.Single(component.FindAll("[aria-label='[[Settings_ExpandDisplayPaneAriaLabel]]']"));
        Assert.DoesNotContain("[[Settings_ExpandDisplayPaneAriaLabel]]", component.Find(".settings-form").TextContent);

        Assert.Contains("[[Settings_PreReleaseLabel]]", component.Markup);
        Assert.Single(component.FindAll("[aria-label='[[Settings_PreReleaseAriaLabel]]']"));
        Assert.DoesNotContain("[[Settings_PreReleaseAriaLabel]]", component.Find(".footer-extra").TextContent);
    }
}
