// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Localization;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.UI.FilterLenses;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.FilterLenses;

/// <summary>
///     Proves <c>LensBreadcrumb</c> routes every chip and chrome string through the localizer (via
///     <see cref="MarkerLocalizer" />) rather than emitting hardcoded literals: the chip text is the formatted
///     <see cref="FilterLensLabel" />, the per-chip keep/remove aria carry that formatted label as an argument, and a
///     ResolutionStatus value routes its closed-set token through its own <c>ResolutionStatus_*</c> key.
/// </summary>
public sealed class LensBreadcrumbLocalizerWiringTests : BunitContext
{
    private readonly IFilterLensCommands _commands = Substitute.For<IFilterLensCommands>();
    private readonly IFilterLensSource _source = Substitute.For<IFilterLensSource>();

    public LensBreadcrumbLocalizerWiringTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _source.Lenses.Returns(ImmutableList<FilterLensSummary>.Empty);
        Services.AddSingleton(_commands);
        Services.AddSingleton(_source);
        Services.AddSingleton(Substitute.For<IAlertDialogService>());
        Services.AddSingleton(Substitute.For<IAnnouncementService>());
        Services.AddSingleton<IStringLocalizer<SharedResource>>(new MarkerLocalizer());
    }

    [Fact]
    public void ChipAndChrome_RouteThroughLocalizer()
    {
        var summary = new FilterLensSummary(
            FilterLensId.Create(),
            new FilterLensLabel.PropertyComparison(EventProperty.ActivityId, IsEqual: true, "abc"));
        _source.Lenses.Returns(ImmutableList.Create(summary));

        var cut = Render<LensBreadcrumb>();

        Assert.Equal("[[FilterLens_ActiveLensesAria]]", cut.Find(".lens-breadcrumb").GetAttribute("aria-label"));
        Assert.Contains("[[FilterLens_LensesLabel]]", cut.Find(".lens-breadcrumb-label").TextContent);
        Assert.Contains("[[FilterLens_ClearAll]]", cut.Find(".lens-clear").TextContent);

        Assert.Contains("[[FilterLens_Property_ActivityId]] = abc", cut.Find(".lens-chip-label").TextContent);

        Assert.Equal(
            "[[FilterLens_SaveAria([[FilterLens_Property_ActivityId]] = abc)]]",
            cut.Find(".lens-chip-keep").GetAttribute("aria-label"));
        Assert.Equal(
            "[[FilterLens_RemoveAria([[FilterLens_Property_ActivityId]] = abc)]]",
            cut.Find(".lens-chip-remove").GetAttribute("aria-label"));
    }

    [Fact]
    public void ResolutionStatusChip_RoutesClosedSetValueThroughItsOwnKey()
    {
        var summary = new FilterLensSummary(
            FilterLensId.Create(),
            new FilterLensLabel.PropertyComparison(EventProperty.ResolutionStatus, IsEqual: true, ResolutionStatusTokens.NoProvider));
        _source.Lenses.Returns(ImmutableList.Create(summary));

        var cut = Render<LensBreadcrumb>();

        Assert.Contains(
            "[[FilterLens_Property_ResolutionStatus]] = [[ResolutionStatus_NoProvider]]",
            cut.Find(".lens-chip-label").TextContent);
    }
}
