// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.UI.Inputs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace EventLogExpert.UI.Tests.Inputs;

public sealed class ValueSelectTests : BunitContext
{
    public ValueSelectTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void ClearItem_OmitsDataTooltipSoRenderedTextIsUsed_ButValuedOptionKeepsIt()
    {
        // A ClearItem's Value is default, so its DisplayString is empty; the data-tooltip is omitted and the
        // overflow tooltip falls back to the rendered textContent ("All values"). A valued option keeps its
        // DisplayString (which the ValueSelect's converter already renders as the visible label).
        var component = Render<ValueSelect<string>>(parameters => parameters
            .Add(p => p.Value, "x")
            .Add(p => p.ValueChanged, _ => { })
            .Add(p => p.ChildContent, builder =>
            {
                builder.OpenComponent<ValueSelectItem<string>>(0);
                builder.AddAttribute(1, nameof(ValueSelectItem<string>.ClearItem), true);
                builder.AddAttribute(2, nameof(ValueSelectItem<string>.ChildContent),
                    (RenderFragment)(fragment => fragment.AddContent(0, "All values")));
                builder.CloseComponent();

                builder.OpenComponent<ValueSelectItem<string>>(3);
                builder.AddAttribute(4, nameof(ValueSelectItem<string>.Value), "x");
                builder.CloseComponent();
            }));

        var options = component.FindAll("[role='option']");
        var clearOption = options.First(option => option.TextContent.Contains("All values"));
        var valuedOption = options.First(option => !option.TextContent.Contains("All values"));

        Assert.False(clearOption.HasAttribute("data-tooltip"), "ClearItem omits the Value-derived data-tooltip");
        Assert.True(clearOption.HasAttribute("data-tooltip-overflow"));

        Assert.Equal("x", valuedOption.GetAttribute("data-tooltip"));
        Assert.True(valuedOption.HasAttribute("data-tooltip-overflow"));
    }

    [Fact]
    public void Input_WhenFocusedAndTypedTextDoesNotParse_KeepsTypedTextSoItCanBeCorrected()
    {
        int? bound = 7;
        var component = Render<ValueSelect<int?>>(parameters => parameters
            .Add(p => p.IsInput, true)
            .Add(p => p.Value, bound)
            .Add(p => p.ValueChanged, value => bound = value));

        var input = component.Find("input[role='combobox']");
        input.TriggerEvent("onfocus", new FocusEventArgs());
        input.Input("12x");

        // The value clears so consumers cannot submit invalid input, but the input keeps the typed text to be corrected.
        Assert.Null(bound);
        Assert.Equal("12x", component.Find("input[role='combobox']").GetAttribute("value"));
    }

    [Fact]
    public void Input_WhenTypedTextDoesNotParse_ClearsValueInsteadOfKeepingStaleOne()
    {
        // Invalid editable numeric text must clear the value so consumers cannot submit stale data.
        int? bound = 7;
        var component = Render<ValueSelect<int?>>(parameters => parameters
            .Add(p => p.IsInput, true)
            .Add(p => p.Value, bound)
            .Add(p => p.ValueChanged, value => bound = value));

        component.Find("input[role='combobox']").Input("not-a-number");

        Assert.Null(bound);
    }

    [Fact]
    public void Input_WhenTypedTextParses_RaisesValueChangedWithParsedValue()
    {
        int? bound = 7;
        var component = Render<ValueSelect<int?>>(parameters => parameters
            .Add(p => p.IsInput, true)
            .Add(p => p.Value, bound)
            .Add(p => p.ValueChanged, value => bound = value));

        component.Find("input[role='combobox']").Input("12");

        Assert.Equal(12, bound);
    }

    [Fact]
    public void Item_RendersOverflowFocusTooltipAndNoNativeTitle()
    {
        var options = new[] { (Value: "a", Name: "Alpha"), (Value: "b", Name: "Beta") };

        var component = Render<ValueSelect<string>>(parameters => parameters
            .Add(p => p.Value, "a")
            .Add(p => p.ValueChanged, _ => { })
            .Add(p => p.ChildContent, builder =>
            {
                var seq = 0;

                foreach (var option in options)
                {
                    builder.OpenComponent<ValueSelectItem<string>>(seq++);
                    builder.AddAttribute(seq++, nameof(ValueSelectItem<string>.Value), option.Value);
                    builder.AddAttribute(seq++, nameof(ValueSelectItem<string>.AriaLabel), option.Name);
                    builder.CloseComponent();
                }
            }));

        foreach (var option in component.FindAll("[role='option']"))
        {
            Assert.True(option.HasAttribute("data-tooltip"));
            Assert.True(option.HasAttribute("data-tooltip-overflow"));
            Assert.False(option.HasAttribute("title"));
        }

        var input = component.Find("input[role='combobox']");
        Assert.True(input.HasAttribute("data-tooltip"));
        Assert.True(input.HasAttribute("data-tooltip-overflow"));
        Assert.False(input.HasAttribute("title"));
    }

    [Fact]
    public void Item_WithAriaLabelAndDescription_PinsNameAndWiresUniqueDescribedByPerOption()
    {
        var options = new[]
        {
            (Value: "a", Name: "Alpha", Description: "The first option, described"),
            (Value: "b", Name: "Beta", Description: "The second option, described"),
        };

        var component = Render<ValueSelect<string>>(parameters => parameters
            .Add(p => p.Value, "a")
            .Add(p => p.ValueChanged, _ => { })
            .Add(p => p.ChildContent, builder =>
            {
                var seq = 0;

                foreach (var option in options)
                {
                    builder.OpenComponent<ValueSelectItem<string>>(seq++);
                    builder.AddAttribute(seq++, nameof(ValueSelectItem<string>.Value), option.Value);
                    builder.AddAttribute(seq++, nameof(ValueSelectItem<string>.AriaLabel), option.Name);
                    builder.AddAttribute(seq++, nameof(ValueSelectItem<string>.Description), option.Description);
                    builder.CloseComponent();
                }
            }));

        var rendered = component.FindAll("[role='option']");
        Assert.Equal(2, rendered.Count);

        // Name is pinned via aria-label so the hidden description span cannot pollute the option's accessible name.
        Assert.Equal("Alpha", rendered[0].GetAttribute("aria-label"));
        Assert.Equal("Beta", rendered[1].GetAttribute("aria-label"));

        // Each option's describedby is unique (per-item ComponentId) and resolves to a hidden span with the text.
        var firstDescribedBy = rendered[0].GetAttribute("aria-describedby")!;
        var secondDescribedBy = rendered[1].GetAttribute("aria-describedby")!;
        Assert.False(string.IsNullOrWhiteSpace(firstDescribedBy));
        Assert.NotEqual(firstDescribedBy, secondDescribedBy);
        Assert.Equal("The first option, described", component.Find($"#{firstDescribedBy}").TextContent);
        Assert.Equal("The second option, described", component.Find($"#{secondDescribedBy}").TextContent);
    }

    [Fact]
    public void Keyboard_ArrowDownOnClosedSelect_OpensDropdownWithoutChangingValue()
    {
        string bound = "b";
        var component = RenderSelectOnly(bound, value => bound = value);

        component.Find(".dropdown-input").TriggerEvent("onkeydown", new KeyboardEventArgs { Code = "ArrowDown" });

        Assert.Equal("true", component.Find("input[role='combobox']").GetAttribute("aria-expanded"));
        Assert.Equal("b", bound);
    }

    [Fact]
    public void Keyboard_ArrowDownWhenOpen_NavigatesToNextValue()
    {
        string bound = "a";
        var component = RenderSelectOnly(bound, value => bound = value);

        // First press opens the dropdown without moving the selection.
        component.Find(".dropdown-input").TriggerEvent("onkeydown", new KeyboardEventArgs { Code = "ArrowDown" });
        Assert.Equal("a", bound);

        // Second press, now that the list is visible, navigates to the next option.
        component.Find(".dropdown-input").TriggerEvent("onkeydown", new KeyboardEventArgs { Code = "ArrowDown" });
        Assert.Equal("b", bound);
    }

    [Fact]
    public void Keyboard_EnterOnClosedSelect_OpensDropdownWithoutChangingValue()
    {
        string bound = "b";
        var component = RenderSelectOnly(bound, value => bound = value);
        Assert.Equal("false", component.Find("input[role='combobox']").GetAttribute("aria-expanded"));

        component.Find(".dropdown-input").TriggerEvent("onkeydown", new KeyboardEventArgs { Code = "Enter" });

        // A collapsed combobox opens on Enter and must not change the selected value.
        Assert.Equal("true", component.Find("input[role='combobox']").GetAttribute("aria-expanded"));
        Assert.Equal("b", bound);
    }

    [Fact]
    public void Keyboard_NumpadEnterOnClosedSelect_OpensDropdownWithoutChangingValue()
    {
        string bound = "b";
        var component = RenderSelectOnly(bound, value => bound = value);

        // Keypad Enter arrives as Code "NumpadEnter" but Key "Enter"; it must behave like the main Enter.
        component.Find(".dropdown-input").TriggerEvent("onkeydown", new KeyboardEventArgs { Code = "NumpadEnter", Key = "Enter" });

        Assert.Equal("true", component.Find("input[role='combobox']").GetAttribute("aria-expanded"));
        Assert.Equal("b", bound);
    }

    [Fact]
    public void Keyboard_SpaceOnClosedInput_DoesNotOpenDropdown()
    {
        int? bound = 7;
        var component = Render<ValueSelect<int?>>(parameters => parameters
            .Add(p => p.IsInput, true)
            .Add(p => p.Value, bound)
            .Add(p => p.ValueChanged, value => bound = value));

        component.Find(".dropdown-input").TriggerEvent("onkeydown", new KeyboardEventArgs { Code = "Space" });

        // Space types into an editable (IsInput) combobox rather than opening it.
        Assert.Equal("false", component.Find("input[role='combobox']").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void Keyboard_SpaceOnClosedSelect_OpensDropdownWithoutChangingValue()
    {
        string bound = "b";
        var component = RenderSelectOnly(bound, value => bound = value);

        component.Find(".dropdown-input").TriggerEvent("onkeydown", new KeyboardEventArgs { Code = "Space" });

        // A collapsed select-only combobox opens on Space without changing the selected value.
        Assert.Equal("true", component.Find("input[role='combobox']").GetAttribute("aria-expanded"));
        Assert.Equal("b", bound);
    }

    [Fact]
    public void Render_AriaDescribedBy_AppliedToCombobox()
    {
        var component = Render<ValueSelect<string>>(parameters => parameters
            .Add(p => p.AriaDescribedBy, "help-text-id"));

        var combobox = component.Find("input[role='combobox']");
        Assert.Equal("help-text-id", combobox.GetAttribute("aria-describedby"));
    }

    [Fact]
    public void Render_AriaLabel_AppliedToCombobox()
    {
        var component = Render<ValueSelect<string>>(parameters => parameters
            .Add(p => p.AriaLabel, "Highlight Color"));

        var combobox = component.Find("input[role='combobox']");
        Assert.Equal("Highlight Color", combobox.GetAttribute("aria-label"));
    }

    [Fact]
    public void Render_AriaLabelledByAndAriaLabel_SuppressesAriaLabelPerWaiAriaPrecedence()
    {
        var component = Render<ValueSelect<string>>(parameters => parameters
            .Add(p => p.AriaLabel, "Should be suppressed")
            .Add(p => p.AriaLabelledBy, "external-label-id"));

        var combobox = component.Find("input[role='combobox']");
        Assert.False(combobox.HasAttribute("aria-label"));
        Assert.Equal("external-label-id", combobox.GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void Render_AriaLabelledBy_AppliedToCombobox()
    {
        var component = Render<ValueSelect<string>>(parameters => parameters
            .Add(p => p.AriaLabelledBy, "external-label-id"));

        var combobox = component.Find("input[role='combobox']");
        Assert.Equal("external-label-id", combobox.GetAttribute("aria-labelledby"));
    }

    private IRenderedComponent<ValueSelect<string>> RenderSelectOnly(string value, Action<string> onChanged) =>
        Render<ValueSelect<string>>(parameters => parameters
            .Add(p => p.Value, value)
            .Add(p => p.ValueChanged, onChanged)
            .Add(p => p.ChildContent, builder =>
            {
                var seq = 0;

                foreach (var option in new[] { "a", "b", "c" })
                {
                    builder.OpenComponent<ValueSelectItem<string>>(seq++);
                    builder.AddAttribute(seq++, nameof(ValueSelectItem<string>.Value), option);
                    builder.CloseComponent();
                }
            }));
}
