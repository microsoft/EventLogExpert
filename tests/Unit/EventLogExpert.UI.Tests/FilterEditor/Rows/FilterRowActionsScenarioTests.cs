// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using AngleSharp.Dom;
using Bunit;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Localization;
using EventLogExpert.UI.FilterEditor;
using EventLogExpert.UI.FilterEditor.Rows;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Tests.FilterEditor.Rows;

public sealed class FilterRowActionsScenarioTests : BunitContext
{
    public FilterRowActionsScenarioTests()
    {
        Services.AddSingleton<IStringLocalizer<SharedResource>>(new MarkerLocalizer());
    }

    [Fact]
    public void AuthoringDisabled_RendersNoCopyButton()
    {
        var context = new ScenarioAuthoringRowContext(Enabled: false, _ => Task.CompletedTask);

        var component = Render<FilterRowActions>(parameters => parameters
            .Add(p => p.Value, MakeSavedFilter())
            .AddCascadingValue(context));

        Assert.Null(FindScenarioButton(component));
    }

    [Fact]
    public async Task AuthoringEnabled_RendersCopyButton_AndClickInvokesCopyWithRowFilter()
    {
        var savedFilter = MakeSavedFilter();
        SavedFilter? copied = null;
        var context = new ScenarioAuthoringRowContext(Enabled: true, filter =>
        {
            copied = filter;

            return Task.CompletedTask;
        });

        var component = Render<FilterRowActions>(parameters => parameters
            .Add(p => p.Value, savedFilter)
            .AddCascadingValue(context));

        var button = FindScenarioButton(component);
        Assert.NotNull(button);

        await button!.ClickAsync(new MouseEventArgs());

        Assert.Same(savedFilter, copied);
    }

    [Fact]
    public void NoAuthoringContext_RendersNoCopyButton()
    {
        var component = Render<FilterRowActions>(parameters => parameters
            .Add(p => p.Value, MakeSavedFilter()));

        Assert.Null(FindScenarioButton(component));
    }

    private static IElement? FindScenarioButton(IRenderedComponent<FilterRowActions> component) =>
        component.FindAll("button")
            .FirstOrDefault(button => button.GetAttribute("aria-label")?.Contains("FilterEditor_RowAction_CopyScenarioAria") == true);

    private static SavedFilter MakeSavedFilter() =>
        new()
        {
            ComparisonText = "Id == 1000",
            Compiled = null,
            IsEnabled = true,
        };
}
