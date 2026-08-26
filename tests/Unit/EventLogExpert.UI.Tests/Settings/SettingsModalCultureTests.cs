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
using System.Globalization;

namespace EventLogExpert.UI.Tests.Settings;

/// <summary>
///     SettingsModal test that MUTATES thread UI culture (qps-Ploc); the base context restores both cultures on
///     dispose and the class is serialized via <see cref="CultureSensitiveCollection" />.
/// </summary>
[Collection(CultureSensitiveCollection.Name)]
public sealed class SettingsModalCultureTests : CultureSensitiveBunitContext
{
    private readonly IAnnouncementService _announcementService = Substitute.For<IAnnouncementService>();
    private readonly IDetailsPanePreferencesProvider _detailsPanePreferences = Substitute.For<IDetailsPanePreferencesProvider>();
    private readonly IModalCoordinator _modalCoordinator = Substitute.For<IModalCoordinator>();
    private readonly IModalService _modalService = Substitute.For<IModalService>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();

    public SettingsModalCultureTests()
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
    public void SettingsModal_UnderPseudoLocale_LoadsSatelliteButRendersEnglish()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("qps-ploc");

        // Positive control: the qps-Ploc satellite IS loaded, so "assert English" below is not vacuous.
        var localizer = Services.GetRequiredService<IStringLocalizer<SharedResource>>();
        Assert.Equal("[qps-ploc] canary probe one", localizer["Canary_Probe1"].Value);

        // Real Settings keys are absent from the canary satellite -> neutral English (a qps-Ploc machine never renders pseudo UI).
        var component = Render<SettingsModal>();
        Assert.Equal("Time Zone:", component.Find("label[for='settings-timezone']").TextContent.Trim());
        Assert.Equal("Settings", component.Find("dialog").GetAttribute("aria-label"));
    }
}
