// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Localization;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using System.Globalization;

namespace EventLogExpert.UI.Tests.Dashboard;

/// <summary>
///     Dashboard tests that MUTATE thread UI culture (qps-Ploc canary). Restores culture synchronously and is
///     serialized via <see cref="CultureSensitiveCollection" />. Proves a pseudo-locale machine loads the canary satellite
///     yet real Dashboard keys FALL BACK to neutral English (the satellite carries only the dedicated canary probes).
/// </summary>
[Collection(CultureSensitiveCollection.Name)]
public sealed class DashboardCultureTests : BunitContext
{
    public DashboardCultureTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddEventLogLocalization();
    }

    [Fact]
    public void Canary_UnderPseudoLocale_LoadsSatellite_AndRealDashboardKeysFallBackToNeutral()
    {
        var priorUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            var localizer = Services.GetRequiredService<IStringLocalizer<SharedResource>>();
            (string Key, string NeutralValue)[] neutralValues =
            [
                ("Dashboard_QuickLaunch", localizer["Dashboard_QuickLaunch"].Value),
                ("Dashboard_Launch", localizer["Dashboard_Launch"].Value),
                ("Dashboard_Group_SystemHealth", localizer["Dashboard_Group_SystemHealth"].Value)
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
