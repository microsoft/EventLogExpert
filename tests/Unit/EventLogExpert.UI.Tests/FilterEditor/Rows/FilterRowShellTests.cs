// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.UI.FilterEditor.Rows;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Tests.FilterEditor.Rows;

public sealed class FilterRowShellTests : BunitContext
{
    [Fact]
    public void SavedFilter_AllButtons_HaveTypeButton()
    {
        var savedFilter = MakeSavedFilter(isEnabled: true);

        var component = RenderChrome(value: savedFilter);

        var buttons = component.FindAll("button");
        Assert.NotEmpty(buttons);
        foreach (var button in buttons)
        {
            Assert.Equal("button", button.GetAttribute("type"));
        }
    }

    [Fact]
    public void SavedFilter_Disabled_RendersToggleNotVerbButton()
    {
        var savedFilter = MakeSavedFilter(isEnabled: false);

        var component = RenderChrome(value: savedFilter);

        var enableButton = component.FindAll("button")
            .FirstOrDefault(b => b.TextContent.Contains("Enable"));
        Assert.Null(enableButton);

        var toggle = component.Find("input[role='switch']");
        Assert.False(toggle.HasAttribute("checked"));
    }

    [Fact]
    public void SavedFilter_Enabled_RendersToggleNotVerbButton()
    {
        var savedFilter = MakeSavedFilter(isEnabled: true);

        var component = RenderChrome(value: savedFilter);

        var disableButton = component.FindAll("button")
            .FirstOrDefault(b => b.TextContent.Contains("Disable"));
        Assert.Null(disableButton);

        var toggle = component.Find("input[role='switch']");
        Assert.True(toggle.HasAttribute("checked"));

        Assert.False(toggle.HasAttribute("aria-label"));
        Assert.False(string.IsNullOrEmpty(toggle.GetAttribute("aria-labelledby")));
    }

    [Fact]
    public void SavedFilter_Toggle_AriaLabelledByTargetsVisibleComparisonText()
    {
        var savedFilter = MakeSavedFilter(isEnabled: true);

        var component = RenderChrome(value: savedFilter);

        var toggle = component.Find("input[role='switch']");
        var labelIds = toggle.GetAttribute("aria-labelledby");
        Assert.False(string.IsNullOrWhiteSpace(labelIds));

        // aria-labelledby may chain multiple IDs (filter label + toggle purpose hint); verify the
        // comparison text appears in at least one of the referenced elements.
        var referencedTexts = labelIds!.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(id => component.Find($"#{id}").TextContent);
        Assert.Contains(referencedTexts, text => text.Contains(savedFilter.ComparisonText));
    }

    [Fact]
    public async Task Toggle_Change_InvokesOnToggleEnabled()
    {
        var savedFilter = MakeSavedFilter(isEnabled: false);
        int invocations = 0;

        var component = Render<FilterRowShell>(parameters => parameters
            .Add(p => p.Value, savedFilter)
            .Add(p => p.OnToggleEnabled,
                EventCallback.Factory.Create(this, () => invocations++)));

        var toggle = component.Find("input[role='switch']");
        await toggle.ChangeAsync(new ChangeEventArgs { Value = true });

        Assert.Equal(1, invocations);
    }

    private static SavedFilter MakeSavedFilter(bool isEnabled) =>
        new()
        {
            ComparisonText = "Id == 1000",
            Compiled = null,
            IsEnabled = isEnabled,
        };

    private IRenderedComponent<FilterRowShell> RenderChrome(SavedFilter? value) =>
        Render<FilterRowShell>(parameters => parameters
            .Add(p => p.Value, value));
}
