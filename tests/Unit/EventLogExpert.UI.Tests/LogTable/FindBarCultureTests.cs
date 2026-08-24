// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.UI.LogTable.Find;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using System.Globalization;

namespace EventLogExpert.UI.Tests.LogTable;

/// <summary>
///     FindBar tests that MUTATE thread culture (qps-Ploc canary, de-DE formatting); each restores culture
///     synchronously and the class is serialized via <see cref="CultureSensitiveCollection" />.
/// </summary>
[Collection(CultureSensitiveCollection.Name)]
public sealed class FindBarCultureTests : BunitContext
{
    public FindBarCultureTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/EventLogExpert.UI/LogTable/Find/FindBar.razor.js");
        Services.AddEventLogLocalization();
    }

    [Fact]
    public void Canary_UnderPseudoLocale_LoadsSatellite_AndRealKeysFallBackToNeutral()
    {
        var priorUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("qps-ploc");
            var localizer = Services.GetRequiredService<IStringLocalizer<SharedResource>>();

            var canary = localizer["Canary_Probe1"];
            Assert.False(canary.ResourceNotFound);
            Assert.Equal("[qps-ploc] canary probe one", canary.Value);

            // A real key is absent from the canary satellite -> neutral English, proving a qps-Ploc machine never renders pseudo UI.
            Assert.Equal("No results", localizer["FindBar_NoResults"].Value);
        }
        finally
        {
            CultureInfo.CurrentUICulture = priorUiCulture;
        }
    }

    [Fact]
    public void Count_UnderNonInvariantCulture_UsesLocaleGroupSeparators()
    {
        var priorCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            var cut = Render<FindBar>(parameters => parameters
                .Add(component => component.Query, "x")
                .Add(component => component.IsScanning, false)
                .Add(component => component.MatchCount, 5678)
                .Add(component => component.CurrentOrdinal, 1234));

            Assert.Equal("1.234/5.678", cut.Find(".find-count").TextContent.Trim());
        }
        finally
        {
            CultureInfo.CurrentCulture = priorCulture;
        }
    }
}
