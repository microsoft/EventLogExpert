// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.UI.Common;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Inputs;

public sealed partial class TagPicker : ComponentBase
{
    private readonly string _listboxId = ComponentId.NewUnique("tag-picker-listbox").Value;

    private int _activeOptionIndex = -1;
    private IReadOnlyList<string> _filteredSuggestions = [];
    private ElementReference _inputRef;
    private string _inputText = string.Empty;
    private bool _isDropdownOpen;
    private bool _isInputFocused;

    [Parameter] public string? AriaLabel { get; set; }

    [Parameter] public string? Placeholder { get; set; } = "Add tag…";

    [Parameter][EditorRequired] public required IReadOnlyList<string> SuggestionSource { get; set; }

    [Parameter][EditorRequired] public required ImmutableList<string> Value { get; set; }

    [Parameter] public EventCallback<ImmutableList<string>> ValueChanged { get; set; }

    private string EffectiveAriaLabel => string.IsNullOrWhiteSpace(AriaLabel) ? "Tags" : AriaLabel;

    private bool IsListboxOpen => _isDropdownOpen && _isInputFocused && _filteredSuggestions.Count > 0;

    protected override void OnParametersSet()
    {
        RecomputeFilteredSuggestions();
        ClampActiveIndex();
    }

    private void ClampActiveIndex()
    {
        if (_filteredSuggestions.Count == 0)
        {
            _activeOptionIndex = -1;

            return;
        }

        if (_activeOptionIndex >= _filteredSuggestions.Count) { _activeOptionIndex = _filteredSuggestions.Count - 1; }

        if (_activeOptionIndex < 0) { _activeOptionIndex = 0; }
    }

    private async Task CommitInputAsTagAsync()
    {
        if (string.IsNullOrWhiteSpace(_inputText)) { return; }

        var candidate = _inputText;
        _inputText = string.Empty;

        var normalized = LibraryEntryTagNormalizer.Normalize(Value.Concat([candidate]));

        if (!normalized.SequenceEqual(Value, StringComparer.Ordinal))
        {
            Value = normalized;
            await ValueChanged.InvokeAsync(Value);
        }

        RecomputeFilteredSuggestions();
        _activeOptionIndex = _filteredSuggestions.Count > 0 ? 0 : -1;
    }

    private async Task CommitSuggestionAsync(string suggestion)
    {
        _inputText = string.Empty;

        var normalized = LibraryEntryTagNormalizer.Normalize(Value.Concat([suggestion]));

        if (!normalized.SequenceEqual(Value, StringComparer.Ordinal))
        {
            Value = normalized;
            await ValueChanged.InvokeAsync(Value);
        }

        RecomputeFilteredSuggestions();
        _activeOptionIndex = _filteredSuggestions.Count > 0 ? 0 : -1;
    }

    private async Task HandleKeyDownAsync(KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "ArrowDown":
                _isDropdownOpen = true;
                if (_filteredSuggestions.Count > 0)
                {
                    _activeOptionIndex = (_activeOptionIndex + 1) % _filteredSuggestions.Count;
                }
                return;

            case "ArrowUp":
                _isDropdownOpen = true;
                if (_filteredSuggestions.Count > 0)
                {
                    _activeOptionIndex = _activeOptionIndex <= 0
                        ? _filteredSuggestions.Count - 1
                        : _activeOptionIndex - 1;
                }
                return;

            case "Enter":
                if (_isDropdownOpen && _activeOptionIndex >= 0 && _activeOptionIndex < _filteredSuggestions.Count)
                {
                    await CommitSuggestionAsync(_filteredSuggestions[_activeOptionIndex]);
                }
                else
                {
                    await CommitInputAsTagAsync();
                }
                return;

            case ",":
            case ";":
                await CommitInputAsTagAsync();
                return;

            case "Escape":
                _isDropdownOpen = false;
                _activeOptionIndex = -1;
                return;

            case "Backspace":
                if (string.IsNullOrEmpty(_inputText) && Value.Count > 0)
                {
                    await RemoveTagAsync(Value.Count - 1);
                }
                return;

            case "Tab":
                _isDropdownOpen = false;
                _activeOptionIndex = -1;
                return;
        }
    }

    private Task OnInputBlurAsync()
    {
        _isInputFocused = false;
        _isDropdownOpen = false;
        _activeOptionIndex = -1;

        return Task.CompletedTask;
    }

    private Task OnInputChangedAsync()
    {
        var separatorIndex = _inputText.IndexOfAny([',', ';']);

        if (separatorIndex >= 0)
        {
            _inputText = _inputText[..separatorIndex];
            return CommitInputAsTagAsync();
        }

        _isDropdownOpen = true;
        RecomputeFilteredSuggestions();
        _activeOptionIndex = _filteredSuggestions.Count > 0 ? 0 : -1;

        return Task.CompletedTask;
    }

    private void OnInputFocus()
    {
        _isInputFocused = true;
        _isDropdownOpen = true;
        RecomputeFilteredSuggestions();
    }

    private async Task OnSuggestionMouseDownAsync(string suggestion)
    {
        await CommitSuggestionAsync(suggestion);
        await _inputRef.FocusAsync();
    }

    private string OptionId(int index) => $"{_listboxId}-opt-{index}";

    private void RecomputeFilteredSuggestions()
    {
        var alreadySelected = new HashSet<string>(Value, StringComparer.OrdinalIgnoreCase);
        var typed = _inputText?.Trim() ?? string.Empty;

        _filteredSuggestions = [.. SuggestionSource
            .Where(s => !alreadySelected.Contains(s))
            .Where(s => typed.Length == 0 || s.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)];
    }

    private async Task RemoveTagAsync(int index)
    {
        if (index < 0 || index >= Value.Count) { return; }

        Value = Value.RemoveAt(index);
        await ValueChanged.InvokeAsync(Value);

        RecomputeFilteredSuggestions();
        ClampActiveIndex();
    }
}
