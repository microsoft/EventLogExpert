// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.UI.FilterLenses;
using Fluxor;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.FilterLenses;

public sealed class LensBreadcrumbTests : BunitContext
{
    private readonly IFilterLensCommands _commands = Substitute.For<IFilterLensCommands>();
    private readonly IState<FilterLensState> _lensState = Substitute.For<IState<FilterLensState>>();

    public LensBreadcrumbTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_commands);
        Services.AddSingleton(_lensState);
        Services.AddFluxor(options => options.ScanAssemblies(typeof(LensBreadcrumb).Assembly));
    }

    [Fact]
    public void ClearAllButton_DispatchesClearLenses()
    {
        _lensState.Value.Returns(new FilterLensState { Lenses = [Lens("x")] });

        var cut = Render<LensBreadcrumb>();

        cut.Find(".lens-clear").Click();

        _commands.Received(1).ClearLenses();
    }

    [Fact]
    public void Escape_WithinBreadcrumb_PopsTopLens()
    {
        var older = Lens("older");
        var top = Lens("top");
        _lensState.Value.Returns(new FilterLensState { Lenses = [older, top] });

        var cut = Render<LensBreadcrumb>();

        cut.Find(".lens-breadcrumb").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        _commands.Received(1).RemoveLens(top);
    }

    [Fact]
    public void NoLenses_RendersNothing()
    {
        _lensState.Value.Returns(new FilterLensState());

        var cut = Render<LensBreadcrumb>();

        Assert.Empty(cut.FindAll(".lens-breadcrumb"));
    }

    [Fact]
    public void WithLens_RendersChip_AndRemoveButtonDispatchesRemoveLens()
    {
        var lens = Lens("Activity ID = abc");
        _lensState.Value.Returns(new FilterLensState { Lenses = [lens] });

        var cut = Render<LensBreadcrumb>();

        Assert.Contains("Activity ID = abc", cut.Markup);

        cut.Find(".lens-chip-remove").Click();

        _commands.Received(1).RemoveLens(lens);
    }

    private static FilterLens Lens(string label) => new() { Label = label, Kind = LensKind.Property };
}
