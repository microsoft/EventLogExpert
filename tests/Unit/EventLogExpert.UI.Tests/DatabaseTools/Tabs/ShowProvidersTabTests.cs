// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.Runtime.DatabaseTools;
using EventLogExpert.Runtime.DatabaseTools.Elevation;
using EventLogExpert.UI.DatabaseTools.Tabs;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.DatabaseTools.Tabs;

public sealed class ShowProvidersTabTests : BunitContext
{
    public ShowProvidersTabTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddDatabaseToolsTabDependencies();
        Services.AddMenuMocks();
    }

    [Fact]
    public void IncludeProtectedProviders_ShieldsRunAndRoutesThroughElevatedHelper()
    {
        // Opting to include protected providers must shield Run (the click now elevates) and route the run through the
        // elevated helper, not the in-process service.
        var elevatedRunner = ConfigureElevatedShowSucceeded();

        var component = Render<ShowProvidersTab>();
        component.Find("#show-include-protected").Change(true);

        Assert.NotEmpty(component.FindAll(".bi-shield-lock"));

        component.Find(".button-green").Click();

        component.WaitForAssertion(() => AssertShowRoutedThroughElevatedHelper(elevatedRunner));
    }

    [Fact]
    public void LocalScanNonAdmin_OffersProtectedProvidersChoice_RunNotShielded()
    {
        // Empty source (live local providers) on a non-admin process: the fast in-process scan is the default, so Run
        // is not shielded; the "include protected providers" checkbox is offered to opt into elevation.
        var component = Render<ShowProvidersTab>();

        Assert.NotNull(component.Find("#show-include-protected"));
        Assert.Empty(component.FindAll(".bi-shield-lock"));
        Assert.NotEmpty(component.FindAll(".bi-play-fill"));
    }

    [Fact]
    public void Renders_FilterErrorState_WhenInvalidRegexEntered()
    {
        var component = Render<ShowProvidersTab>();

        var filter = component.Find("#show-filter");
        filter.Input("[unterminated");

        Assert.NotEmpty(component.FindAll(".filter-error"));
    }

    [Fact]
    public void Renders_HappyPath_WithExpectedFormFields()
    {
        var component = Render<ShowProvidersTab>();

        Assert.NotNull(component.Find("#show-source-path"));
        Assert.NotNull(component.Find("#show-filter"));
        Assert.NotNull(component.Find(".db-tab-form"));
    }

    [Fact]
    public void Renders_RunButton_PresentInitially_AndNotInCancellingState()
    {
        var component = Render<ShowProvidersTab>();

        Assert.NotNull(component.Find(".button-green"));
        Assert.Empty(component.FindAll(".button-red"));
    }

    [Fact]
    public void RunButton_ExposesElevationToScreenReaders_OnlyWhenElevating()
    {
        // The shield glyph is aria-hidden, so a screen reader's only cue that Run will prompt for elevation is an
        // aria-describedby pointing at a visually-hidden description; it must be present only while the click elevates.
        var component = Render<ShowProvidersTab>();

        Assert.Null(component.Find(".button-green").GetAttribute("aria-describedby"));
        Assert.Empty(component.FindAll("#show-run-elevation-help"));

        component.Find("#show-include-protected").Change(true);

        Assert.Equal("show-run-elevation-help", component.Find(".button-green").GetAttribute("aria-describedby"));
        Assert.Contains("administrator access", component.Find("#show-run-elevation-help").TextContent);
    }

    // Asserts the show ran through the elevated helper exactly once and never touched the in-process service.
    private void AssertShowRoutedThroughElevatedHelper(IElevatedDatabaseToolsRunner elevatedRunner)
    {
        elevatedRunner.ReceivedWithAnyArgs(1).ShowAsync(default!, default!, default, default);
        Services.GetRequiredService<IDatabaseToolsService>()
            .DidNotReceiveWithAnyArgs().ShowAsync(default!, default!, default, default);
    }

    // Configures the elevated runner's show dispatch to succeed so a run routed through elevation completes.
    private IElevatedDatabaseToolsRunner ConfigureElevatedShowSucceeded()
    {
        var elevatedRunner = Services.GetRequiredService<IElevatedDatabaseToolsRunner>();
        elevatedRunner.ShowAsync(default!, default!, default, default)
            .ReturnsForAnyArgs(Task.FromResult(new DatabaseToolsResult(DatabaseToolsOutcome.Succeeded, null, TimeSpan.Zero)));
        return elevatedRunner;
    }
}
