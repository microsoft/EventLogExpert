// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
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
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("qps-ploc");
            var localizer = Services.GetRequiredService<IStringLocalizer<SharedResource>>();

            var canary = localizer["Canary_Probe1"];
            Assert.False(canary.ResourceNotFound);
            Assert.Equal("[qps-ploc] canary probe one", canary.Value);

            // Real Menu keys are absent from the canary satellite -> neutral English (no pseudo Menu UI on a qps-Ploc machine).
            Assert.Equal("File", localizer["Menu_File"].Value);
            Assert.Equal("Show All Events", localizer["Menu_View_ShowAllEvents"].Value);
            Assert.Equal("Resolution & Coverage", localizer["Menu_View_ResolutionCoverage"].Value);
            Assert.Equal("Close all logs", localizer["CloseAllLogs_Title"].Value);
        }
        finally
        {
            CultureInfo.CurrentUICulture = priorUiCulture;
        }
    }
}
