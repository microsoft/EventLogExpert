// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.UI.Inputs;
using Microsoft.AspNetCore.Components;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.Inputs;

public sealed class TagPickerTests : BunitContext
{
    [Fact]
    public void ChipRemoveButton_HasAccessibleLabel()
    {
        var component = Render<TagPicker>(parameters => parameters
            .Add(p => p.Value, ImmutableList.Create("exchange"))
            .Add(p => p.SuggestionSource, []));

        var removeButton = component.Find(".tag-picker-chip-remove");
        Assert.Equal("Remove exchange", removeButton.GetAttribute("aria-label"));
    }

    [Fact]
    public void DropdownMountsOnInputFocus_ShowsUnselectedSuggestions()
    {
        var component = Render<TagPicker>(parameters => parameters
            .Add(p => p.Value, ImmutableList.Create("alpha"))
            .Add(p => p.SuggestionSource, ["alpha", "beta", "gamma"]));

        var input = component.Find(".tag-picker-input");
        input.Focus();

        var listbox = component.Find(".tag-picker-listbox");
        Assert.Equal("listbox", listbox.GetAttribute("role"));

        var options = component.FindAll(".tag-picker-option");
        Assert.Equal(2, options.Count);
        Assert.Contains(options, o => o.TextContent.Trim() == "beta");
        Assert.Contains(options, o => o.TextContent.Trim() == "gamma");
        Assert.DoesNotContain(options, o => o.TextContent.Trim() == "alpha");
    }

    [Fact]
    public void DropdownNotMountedWhenInputUnfocused()
    {
        var component = Render<TagPicker>(parameters => parameters
            .Add(p => p.Value, ImmutableList<string>.Empty)
            .Add(p => p.SuggestionSource, ["alpha", "beta"]));

        Assert.Empty(component.FindAll(".tag-picker-listbox"));
    }

    [Fact]
    public void EnterKey_CommitsTypedTextAsNewTag_NormalizedLowercase()
    {
        ImmutableList<string>? lastValue = null;

        var component = Render<TagPicker>(parameters => parameters
            .Add(p => p.Value, ImmutableList<string>.Empty)
            .Add(p => p.SuggestionSource, [])
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<ImmutableList<string>>(this, v => lastValue = v)));

        var input = component.Find(".tag-picker-input");
        input.Input("MyTag");
        input.KeyDown("Enter");

        Assert.NotNull(lastValue);
        Assert.Equal(["mytag"], lastValue);
    }

    [Fact]
    public void EnterKey_DuplicateOfExistingTag_DoesNotAddDuplicate()
    {
        var changes = 0;

        var component = Render<TagPicker>(parameters => parameters
            .Add(p => p.Value, ImmutableList.Create("alpha"))
            .Add(p => p.SuggestionSource, [])
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<ImmutableList<string>>(this, _ => changes++)));

        var input = component.Find(".tag-picker-input");
        input.Input("ALPHA");
        input.KeyDown("Enter");

        Assert.Equal(0, changes);
    }

    [Fact]
    public void EnterKey_WithEmptyInput_DoesNotAddTag()
    {
        var changes = 0;

        var component = Render<TagPicker>(parameters => parameters
            .Add(p => p.Value, ImmutableList<string>.Empty)
            .Add(p => p.SuggestionSource, [])
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<ImmutableList<string>>(this, _ => changes++)));

        var input = component.Find(".tag-picker-input");
        input.KeyDown("Enter");

        Assert.Equal(0, changes);
    }

    [Fact]
    public void InputElementCarriesApgComboboxAriaAttributes()
    {
        var component = Render<TagPicker>(parameters => parameters
            .Add(p => p.Value, ImmutableList<string>.Empty)
            .Add(p => p.SuggestionSource, ["alpha"])
            .Add(p => p.AriaLabel, "Tag picker for testing"));

        var input = component.Find(".tag-picker-input");
        Assert.Equal("combobox", input.GetAttribute("role"));
        Assert.Equal("listbox", input.GetAttribute("aria-haspopup"));
        Assert.Equal("list", input.GetAttribute("aria-autocomplete"));
        Assert.Equal("Tag picker for testing", input.GetAttribute("aria-label"));

        Assert.Equal("false", input.GetAttribute("aria-expanded"));
        Assert.True(string.IsNullOrEmpty(input.GetAttribute("aria-controls")));

        input.Focus();

        var listbox = component.Find(".tag-picker-listbox");
        input = component.Find(".tag-picker-input");
        Assert.Equal("true", input.GetAttribute("aria-expanded"));
        Assert.Equal(listbox.GetAttribute("id"), input.GetAttribute("aria-controls"));
    }

    [Fact]
    public void MaxTagsCap_NotExceededByCommit()
    {
        ImmutableList<string>? lastValue = null;
        var existing = ImmutableList.CreateRange(Enumerable.Range(1, 20).Select(i => $"tag{i}"));

        var component = Render<TagPicker>(parameters => parameters
            .Add(p => p.Value, existing)
            .Add(p => p.SuggestionSource, [])
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<ImmutableList<string>>(this, v => lastValue = v)));

        var input = component.Find(".tag-picker-input");
        input.Input("overflow");
        input.KeyDown("Enter");

        Assert.Null(lastValue);
    }

    [Fact]
    public void RemoveButton_RemovesTagAndInvokesValueChanged()
    {
        ImmutableList<string>? lastValue = null;

        var component = Render<TagPicker>(parameters => parameters
            .Add(p => p.Value, ImmutableList.Create("alpha", "beta"))
            .Add(p => p.SuggestionSource, [])
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<ImmutableList<string>>(this, v => lastValue = v)));

        var removeButton = component.Find(".tag-picker-chip[role='listitem']:first-child .tag-picker-chip-remove");
        removeButton.Click();

        Assert.NotNull(lastValue);
        Assert.Equal(["beta"], lastValue);
    }

    [Fact]
    public void RendersExistingTagsAsChips()
    {
        var component = Render<TagPicker>(parameters => parameters
            .Add(p => p.Value, ImmutableList.Create("alpha", "beta"))
            .Add(p => p.SuggestionSource, ["alpha", "beta", "gamma"]));

        var chips = component.FindAll(".tag-picker-chip");
        Assert.Equal(2, chips.Count);
        Assert.Contains(chips, c => c.TextContent.Contains("alpha", StringComparison.Ordinal));
        Assert.Contains(chips, c => c.TextContent.Contains("beta", StringComparison.Ordinal));
    }

    [Fact]
    public void SeparatorInput_CommitsTypedTextAndClearsSeparator()
    {
        ImmutableList<string>? lastValue = null;

        var component = Render<TagPicker>(parameters => parameters
            .Add(p => p.Value, ImmutableList<string>.Empty)
            .Add(p => p.SuggestionSource, [])
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<ImmutableList<string>>(this, v => lastValue = v)));

        var input = component.Find(".tag-picker-input");
        input.Input("Beta,");

        Assert.NotNull(lastValue);
        Assert.Equal(["beta"], lastValue);
        Assert.Equal(string.Empty, input.GetAttribute("value"));
    }

    [Fact]
    public void TypingFiltersSuggestionDropdown()
    {
        var component = Render<TagPicker>(parameters => parameters
            .Add(p => p.Value, ImmutableList<string>.Empty)
            .Add(p => p.SuggestionSource, ["alpha", "alphabet", "beta"]));

        var input = component.Find(".tag-picker-input");
        input.Focus();
        input.Input("alp");

        var options = component.FindAll(".tag-picker-option");
        Assert.Equal(2, options.Count);
        Assert.Contains(options, o => o.TextContent.Trim() == "alpha");
        Assert.Contains(options, o => o.TextContent.Trim() == "alphabet");
    }
}
