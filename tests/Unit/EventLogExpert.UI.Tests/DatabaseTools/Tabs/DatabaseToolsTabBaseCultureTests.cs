// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.Runtime.DatabaseTools;
using EventLogExpert.UI.DatabaseTools.Tabs;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Globalization;
using TestContext = Xunit.TestContext;

namespace EventLogExpert.UI.Tests.DatabaseTools.Tabs;

[Collection(CultureSensitiveCollection.Name)]
public sealed class DatabaseToolsTabBaseCultureTests : BunitContext
{
    public DatabaseToolsTabBaseCultureTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddDatabaseToolsTabDependencies();
        Services.AddMenuMocks();
    }

    [Fact]
    public void OutcomeDurationUsesCurrentCultureDecimalSeparator()
    {
        CultureInfo culture = CultureInfo.GetCultureInfo("fi-FI");
        Services.GetRequiredService<IDatabaseToolsService>()
            .ShowAsync(default!, default!, default, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs(Task.FromResult(new DatabaseToolsResult(DatabaseToolsOutcome.Succeeded, null, TimeSpan.FromSeconds(1.5))));

        RunUnderCulture(culture, () =>
        {
            IRenderedComponent<ShowProvidersTab> component = Render<ShowProvidersTab>();
            component.Find(".button-green").Click();

            component.WaitForAssertion(() =>
                Assert.Contains(1.5.ToString("F1", culture), component.Markup, StringComparison.Ordinal));
        });
    }

    private static void RunUnderCulture(CultureInfo culture, Action verify)
    {
        CultureInfo priorCulture = CultureInfo.CurrentCulture;
        CultureInfo priorUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            verify();
        }
        finally
        {
            CultureInfo.CurrentCulture = priorCulture;
            CultureInfo.CurrentUICulture = priorUiCulture;
        }
    }
}
