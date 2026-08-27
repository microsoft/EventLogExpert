// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Localization;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using System.Globalization;

namespace EventLogExpert.UI.Tests.Menu;

/// <summary>
///     Menu tests that MUTATE thread UI culture (qps-Ploc canary). Restores culture synchronously and is serialized
///     via <see cref="CultureSensitiveCollection" />. Proves a pseudo-locale machine loads the canary satellite yet real
///     Menu_*/CloseAllLogs_* keys FALL BACK to neutral English.
/// </summary>
[Collection(CultureSensitiveCollection.Name)]
public sealed class MenuCultureTests : BunitContext
{
    public MenuCultureTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddEventLogLocalization();
    }

    [Fact]
    public void Canary_UnderPseudoLocale_LoadsSatellite_AndRealMenuKeysFallBackToNeutral()
    {
        var priorUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            var localizer = Services.GetRequiredService<IStringLocalizer<SharedResource>>();
            (string Key, string NeutralValue)[] neutralValues =
            [
                ("Menu_File", localizer["Menu_File"].Value),
                ("Menu_View_ShowAllEvents", localizer["Menu_View_ShowAllEvents"].Value),
                ("Menu_View_ResolutionCoverage", localizer["Menu_View_ResolutionCoverage"].Value),
                ("CloseAllLogs_Title", localizer["CloseAllLogs_Title"].Value)
            ];

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("qps-ploc");

            var canary = localizer["Canary_Probe1"];
            Assert.False(canary.ResourceNotFound);
            Assert.Equal("[qps-ploc] canary probe one", canary.Value);

            foreach (var (key, neutralValue) in neutralValues)
            {
                var localized = localizer[key];
                Assert.False(localized.ResourceNotFound);
                Assert.Equal(neutralValue, localized.Value);
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = priorUiCulture;
        }
    }
}
