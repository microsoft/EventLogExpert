// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.UI.FilterLenses;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.FilterLenses;

public sealed class LensBreadcrumbTests : BunitContext
{
    private readonly IFilterLensCommands _commands = Substitute.For<IFilterLensCommands>();

    private readonly IFilterLensSource _source = Substitute.For<IFilterLensSource>();

    public LensBreadcrumbTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _source.Lenses.Returns(ImmutableList<FilterLensSummary>.Empty);
        Services.AddSingleton(_commands);
        Services.AddSingleton(_source);
    }

    [Fact]
    public void Changed_ReRendersTheBreadcrumb()
    {
        var cut = Render<LensBreadcrumb>();
        Assert.Empty(cut.FindAll(".lens-breadcrumb"));

        _source.Lenses.Returns(ImmutableList.Create(Summary("Activity ID = abc")));
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
    public void NoLenses_RendersNothing()
    {
        _source.Lenses.Returns(ImmutableList<FilterLensSummary>.Empty);

        var cut = Render<LensBreadcrumb>();

        Assert.Empty(cut.FindAll(".lens-breadcrumb"));
    }

    [Fact]
    public void WithLens_RendersLabel_AndRemoveButtonDispatchesRemoveLens()
    {
        var lens = Summary("Activity ID = abc");
        _source.Lenses.Returns(ImmutableList.Create(lens));

        var cut = Render<LensBreadcrumb>();

        Assert.Contains("Activity ID = abc", cut.Markup);

        cut.Find(".lens-lens-remove").Click();

        _commands.Received(1).RemoveLens(lens.Id);
    }

    private static FilterLensSummary Summary(string label) => new(FilterLensId.Create(), label);
}
