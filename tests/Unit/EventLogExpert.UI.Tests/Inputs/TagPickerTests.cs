// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.UI.Inputs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Collections.Immutable;
using System.Reflection;

namespace EventLogExpert.UI.Tests.Inputs;

public sealed class TagPickerTests : BunitContext
{
    public TagPickerTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/EventLogExpert.UI/Common/focusGuard.js");
    }

    [Fact]
    public void Backspace_WithMultipleTags_DoesNotArmFocusRestore()
    {
        // Backspace from the input removes the last tag but focus is already in the surviving input - it must NOT arm
        // a neighbor restore (that would yank focus onto a chip mid-typing).
        var focusModule = JSInterop.SetupModule("./_content/EventLogExpert.UI/Common/focusGuard.js");

        var cut = Render<TagPicker>(parameters => parameters
            .Add(p => p.Value, ImmutableList.Create("alpha", "beta"))
            .Add(p => p.SuggestionSource, [])
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<ImmutableList<string>>(this, _ => { })));

        cut.Find(".tag-picker-input").KeyDown(new KeyboardEventArgs { Key = "Backspace" });

        Assert.DoesNotContain(focusModule.Invocations, invocation => invocation.Identifier == "focusIfNotElsewhere");
    }

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
    public void DuplicateTagsInValue_RenderWithoutThrowing_AsDistinctChips()
    {
        // @key keys on the tag value; duplicates (only reachable via a corrupt/pre-normalizer source) must not throw a
        // Blazor duplicate-key exception - the render projection dedupes to distinct chips.
        var cut = Render<TagPicker>(parameters => parameters
            .Add(p => p.Value, ImmutableList.Create("alpha", "beta", "alpha"))
            .Add(p => p.SuggestionSource, []));

        Assert.Equal(2, cut.FindAll(".tag-picker-chip").Count);
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
    public void Placeholder_WhenProvided_RendersOnEmptyInput()
    {
        var component = Render<TagPicker>(parameters => parameters
            .Add(p => p.Value, ImmutableList<string>.Empty)
            .Add(p => p.SuggestionSource, [])
            .Add(p => p.Placeholder, "Localized placeholder"));

        var input = component.Find(".tag-picker-input");
        Assert.Equal("Localized placeholder", input.GetAttribute("placeholder"));
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
    public void RemoveChip_GuardUnavailable_FailsClosed_NoBareFocus()
    {
        // TagPicker removal is deferred (the user may have moved focus), so if the guard module throws, the restore
        // must fail CLOSED - no bare FocusAsync fallback that could steal focus.
        var focusModule = JSInterop.SetupModule("./_content/EventLogExpert.UI/Common/focusGuard.js");
        focusModule.Setup<bool>("focusIfNotElsewhere", _ => true).SetException(new JSException("boom"));

        var cut = Render<TagPicker>(parameters => parameters
            .Add(p => p.Value, ImmutableList.Create("alpha", "beta", "gamma"))
            .Add(p => p.SuggestionSource, [])
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<ImmutableList<string>>(this, _ => { })));

        cut.FindAll(".tag-picker-chip-remove")[1].Click();

        Assert.DoesNotContain(JSInterop.Invocations, invocation =>
            invocation.Identifier.Contains("domWrapper.focus", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RemoveChip_RestoreIsSingleShot_AcrossHostRepublishedValues()
    {
        // TagPicker optimistically writes Value locally, so the guarded restore fires once on the post-click render. A
        // host that later re-publishes the value (the pre-removal list, then the authoritative removal - as the async,
        // failable LibraryEntryRow persist does) must NOT trigger a second restore: the arm is single-shot.
        var focusModule = JSInterop.SetupModule("./_content/EventLogExpert.UI/Common/focusGuard.js");

        var cut = Render<TagPicker>(parameters => parameters
            .Add(p => p.Value, ImmutableList.Create("alpha", "beta", "gamma"))
            .Add(p => p.SuggestionSource, [])
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<ImmutableList<string>>(this, _ => { })));

        cut.FindAll(".tag-picker-chip-remove")[1].Click();

        cut.Render(parameters => parameters.Add(p => p.Value, ImmutableList.Create("alpha", "beta", "gamma")));
        cut.Render(parameters => parameters.Add(p => p.Value, ImmutableList.Create("alpha", "gamma")));

        Assert.Single(focusModule.Invocations, invocation => invocation.Identifier == "focusIfNotElsewhere");
    }

    [Fact]
    public void RemoveChip_SyncHost_RestoresFocusToForwardNeighborChip()
    {
        // A synchronous host (e.g. LibrarySavedTabHeader: _draftTags = tags) leaves the local removal in place, so the
        // removed tag is absent on the next render and the restore fires - routed through the orphan guard to the
        // FORWARD neighbor chip's remove button, never a bare FocusAsync and never the wrong element.
        var focusModule = JSInterop.SetupModule("./_content/EventLogExpert.UI/Common/focusGuard.js");

        var cut = Render<TagPicker>(parameters => parameters
            .Add(p => p.Value, ImmutableList.Create("alpha", "beta", "gamma"))
            .Add(p => p.SuggestionSource, [])
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<ImmutableList<string>>(this, _ => { })));

        cut.FindAll(".tag-picker-chip-remove")[1].Click();

        cut.WaitForAssertion(() => focusModule.VerifyInvoke("focusIfNotElsewhere"));
        AssertGuardedFocusTargets(focusModule, RemoveButtonElement(cut, "gamma"));
    }

    [Fact]
    public void RemoveSoleChip_GuardDeclines_DoesNotStrandDropdownSuppression()
    {
        // When the guard declines (focus already moved) the input is not focused, so OnInputFocus never consumes the
        // flag; it must be cleared so the user's next genuine input focus still opens the listbox.
        var focusModule = JSInterop.SetupModule("./_content/EventLogExpert.UI/Common/focusGuard.js");
        focusModule.Setup<bool>("focusIfNotElsewhere", _ => true).SetResult(false);

        var cut = Render<TagPicker>(parameters => parameters
            .Add(p => p.Value, ImmutableList.Create("only"))
            .Add(p => p.SuggestionSource, ["suggestion"])
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<ImmutableList<string>>(this, _ => { })));

        cut.Find(".tag-picker-chip-remove").Click();

        cut.Find(".tag-picker-input").Focus();
        Assert.NotEmpty(cut.FindAll(".tag-picker-listbox"));
    }

    [Fact]
    public void RemoveSoleChip_GuardMovesFocusToInput_SuppressesTheNextDropdownOpen()
    {
        // Removing the only chip restores focus to the always-present input. The suggestion listbox must NOT reopen on
        // that programmatic focus (the just-removed tag would be the active suggestion), but a later genuine focus must.
        var focusModule = JSInterop.SetupModule("./_content/EventLogExpert.UI/Common/focusGuard.js");
        focusModule.Setup<bool>("focusIfNotElsewhere", _ => true).SetResult(true);

        var cut = Render<TagPicker>(parameters => parameters
            .Add(p => p.Value, ImmutableList.Create("only"))
            .Add(p => p.SuggestionSource, ["suggestion"])
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<ImmutableList<string>>(this, _ => { })));

        cut.Find(".tag-picker-chip-remove").Click();
        AssertGuardedFocusTargets(focusModule, InputElement(cut));

        // The restored (programmatic) focus landing on the input keeps the listbox closed...
        cut.Find(".tag-picker-input").Focus();
        Assert.Empty(cut.FindAll(".tag-picker-listbox"));

        // ...but the user's next genuine focus opens it (the one-shot suppression was consumed).
        cut.Find(".tag-picker-input").Focus();
        Assert.NotEmpty(cut.FindAll(".tag-picker-listbox"));
    }

    [Fact]
    public void RemoveTagAriaLabelFormat_WhenProvided_FormatsChipAriaPerTag()
    {
        var component = Render<TagPicker>(parameters => parameters
            .Add(p => p.Value, ImmutableList.Create("exchange"))
            .Add(p => p.SuggestionSource, [])
            .Add(p => p.RemoveTagAriaLabelFormat, "Quitar {0}"));

        var removeButton = component.Find(".tag-picker-chip-remove");
        Assert.Equal("Quitar exchange", removeButton.GetAttribute("aria-label"));
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

    private static ElementReference InputElement(IRenderedComponent<TagPicker> cut) =>
        (ElementReference)typeof(TagPicker)
            .GetField("_inputRef", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!;

    private static ElementReference RemoveButtonElement(IRenderedComponent<TagPicker> cut, string tag)
    {
        var buttons = (Dictionary<string, ChromelessButton?>)typeof(TagPicker)
            .GetField("_removeButtons", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!;

        return buttons[tag]!.Element;
    }

    private void AssertGuardedFocusTargets(BunitJSModuleInterop module, ElementReference expected)
    {
        var call = module.Invocations.Last(invocation => invocation.Identifier == "focusIfNotElsewhere");
        Assert.Equal(expected.Id, ((ElementReference)call.Arguments[0]!).Id);
    }
}
