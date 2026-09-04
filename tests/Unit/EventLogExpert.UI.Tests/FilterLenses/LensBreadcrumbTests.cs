// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using AngleSharp.Dom;
using Bunit;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Localization;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.UI.FilterLenses;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.FilterLenses;

public sealed class LensBreadcrumbTests : BunitContext
{
    private readonly IAlertDialogService _alertDialog = Substitute.For<IAlertDialogService>();

    private readonly IAnnouncementService _announcements = Substitute.For<IAnnouncementService>();
    private readonly IFilterLensCommands _commands = Substitute.For<IFilterLensCommands>();

    private readonly IFilterLensSource _source = Substitute.For<IFilterLensSource>();

    public LensBreadcrumbTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _source.Lenses.Returns(ImmutableList<FilterLensSummary>.Empty);
        Services.AddSingleton(_commands);
        Services.AddSingleton(_source);
        Services.AddSingleton(_alertDialog);
        Services.AddSingleton(_announcements);
        Services.AddEventLogLocalization();
    }

    private IStringLocalizer<SharedResource> Localizer =>
        Services.GetRequiredService<IStringLocalizer<SharedResource>>();

    [Fact]
    public void Changed_ReRendersTheBreadcrumb()
    {
        var cut = Render<LensBreadcrumb>();
        Assert.Empty(cut.FindAll(".lens-breadcrumb"));

        _source.Lenses.Returns(ImmutableList.Create(Summary("abc")));
        _source.Changed += Raise.Event<Action>();

        cut.WaitForAssertion(() => Assert.Contains("Activity ID = abc", cut.Markup));
    }

    [Fact]
    public void ClearAllButton_DispatchesClearLenses()
    {
        _source.Lenses.Returns(ImmutableList.Create(Summary("x")));

        var cut = Render<LensBreadcrumb>();

        cut.Find(".lens-clear").Click();

        _commands.Received(1).ClearLenses();
    }

    [Fact]
    public void Escape_WithinBreadcrumb_PopsTopLens()
    {
        var older = Summary("older");
        var top = Summary("top");
        _source.Lenses.Returns(ImmutableList.Create(older, top));

        var cut = Render<LensBreadcrumb>();

        cut.Find(".lens-breadcrumb").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        _commands.Received(1).RemoveLens(top.Id);
    }

    [Fact]
    public void KeepButton_DispatchesPromoteLens_AndHasAccessibleLabel()
    {
        var lens = Summary("abc");
        _source.Lenses.Returns(ImmutableList.Create(lens));

        var cut = Render<LensBreadcrumb>();

        var keep = cut.Find(".lens-chip-keep");
        Assert.Equal("Save lens as filter: Activity ID = abc", keep.GetAttribute("aria-label"));

        keep.Click();

        _commands.Received(1).PromoteLens(lens.Id);
    }

    [Fact]
    public void NoLenses_RendersNothing()
    {
        _source.Lenses.Returns(ImmutableList<FilterLensSummary>.Empty);

        var cut = Render<LensBreadcrumb>();

        Assert.Empty(cut.FindAll(".lens-breadcrumb"));
    }

    [Fact]
    public void SaveAllButton_PromotesAllLenses_AndAnnounces()
    {
        _source.Lenses.Returns(ImmutableList.Create(Summary("a"), Summary("b")));

        var cut = Render<LensBreadcrumb>();

        SaveActionButton(cut, Localizer["FilterLens_SaveAll"].Value).Click();

        _commands.Received(1).PromoteAllLenses();
        _announcements.Received(1).Announce(Localizer["FilterLens_SavedAllAnnouncement"].Value);
    }

    [Fact]
    public void SaveAsGroupButton_WithOnlyTimeWindowLenses_IsDisabled()
    {
        _source.Lenses.Returns(ImmutableList.Create(TimeSummary()));

        var cut = Render<LensBreadcrumb>();

        Assert.True(SaveActionButton(cut, Localizer["FilterLens_SaveAsGroup"].Value).HasAttribute("disabled"));
    }

    [Fact]
    public async Task SaveAsGroupButton_WithValueLens_PromptsAndSavesGroup()
    {
        _source.Lenses.Returns(ImmutableList.Create(Summary("a")));
        _alertDialog.DisplayPrompt(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns("My Group");

        var cut = Render<LensBreadcrumb>();

        var button = SaveActionButton(cut, Localizer["FilterLens_SaveAsGroup"].Value);
        Assert.False(button.HasAttribute("disabled"));

        await button.ClickAsync(new MouseEventArgs());

        _commands.Received(1).SaveLensesAsGroup("My Group");
        _announcements.Received(1).Announce(Localizer["FilterLens_SavedAsGroupAnnouncement", "My Group"].Value);
    }

    [Fact]
    public void WithLens_RendersLabel_AndRemoveButtonDispatchesRemoveLens()
    {
        var lens = Summary("abc");
        _source.Lenses.Returns(ImmutableList.Create(lens));

        var cut = Render<LensBreadcrumb>();

        Assert.Contains("Activity ID = abc", cut.Markup);

        cut.Find(".lens-chip-remove").Click();

        _commands.Received(1).RemoveLens(lens.Id);
    }

    private static IElement SaveActionButton(IRenderedComponent<LensBreadcrumb> cut, string text) =>
        cut.FindAll(".lens-action").Single(button => button.TextContent.Trim() == text);

    private static FilterLensSummary Summary(string value) =>
        new(FilterLensId.Create(), new FilterLensLabel.PropertyComparison(EventProperty.ActivityId, IsEqual: true, value));

    private static FilterLensSummary TimeSummary() =>
        new(FilterLensId.Create(), new FilterLensLabel.TimeWindow(DateTime.Now, TimeSpan.FromHours(1)), LensKind.TimeWindow);
}
