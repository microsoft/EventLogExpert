// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.Common.Interop;
using EventLogExpert.UI.Focus;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Collections.Immutable;
using System.Globalization;

namespace EventLogExpert.UI.Inputs;

public sealed partial class TagPicker : ComponentBase, IAsyncDisposable
{
    private readonly string _listboxId = ComponentId.NewUnique("tag-picker-listbox").Value;
    private readonly Dictionary<string, ChromelessButton?> _removeButtons = [];

    private int _activeOptionIndex = -1;
    private IReadOnlyList<string> _filteredSuggestions = [];
    private IJSObjectReference? _focusModule;
    private ElementReference _inputRef;
    private string _inputText = string.Empty;
    private bool _isDropdownOpen;
    private bool _isInputFocused;
    private string? _pendingRemovedTag;
    private IReadOnlyList<string>? _pendingTargetTags;
    private List<string> _renderedTags = [];
    private bool _suppressDropdownOnNextFocus;

    [Parameter][EditorRequired] public string? AriaLabel { get; set; }

    [Parameter][EditorRequired] public string? Placeholder { get; set; } = "Add tag…";

    [Parameter][EditorRequired] public string RemoveTagAriaLabelFormat { get; set; } = "Remove {0}";

    [Parameter][EditorRequired] public required IReadOnlyList<string> SuggestionSource { get; set; }

    [Parameter][EditorRequired] public required ImmutableList<string> Value { get; set; }

    [Parameter] public EventCallback<ImmutableList<string>> ValueChanged { get; set; }

    private string? EffectiveAriaLabel => AriaLabel;

    private bool IsListboxOpen => _isDropdownOpen && _isInputFocused && _filteredSuggestions.Count > 0;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    public async ValueTask DisposeAsync()
    {
        await JsModuleInterop.DisposeModuleSafelyAsync(_focusModule);
        _focusModule = null;

        GC.SuppressFinalize(this);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        PruneRemoveButtons();

        // The host can re-publish the removed tag before the async persist lands, so consume the arm only once the tag
        // is actually gone from Value (single-shot; a stale arm whose persist failed dies when the picker unmounts).
        if (_pendingRemovedTag is { } removedTag && !Value.Contains(removedTag, StringComparer.Ordinal))
        {
            var targetTags = _pendingTargetTags;

            _pendingRemovedTag = null;
            _pendingTargetTags = null;

            await RestoreNeighborOrInputFocusAsync(targetTags);
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override void OnParametersSet()
    {
        RecomputeFilteredSuggestions();
        ClampActiveIndex();
    }

    // Snapshots the deduped tags BEFORE removal and records the ordered fallback chips (nearest forward, then nearest
    // backward) to focus once the removal propagates. Records the whole ordered set, not just the nearest, so an
    // overlapping async removal that deletes the nearest candidate before the arm consumes still lands on a surviving
    // chip (or the input when none remain), never the document body.
    private void ArmNeighborFocus(string removedTag)
    {
        int removedIndex = _renderedTags.IndexOf(removedTag);

        if (removedIndex < 0) { return; }

        _pendingRemovedTag = removedTag;
        _pendingTargetTags = NeighborFocus.GetFallbackNeighbors(_renderedTags, removedIndex);
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
                if (string.IsNullOrEmpty(_inputText) && _renderedTags.Count > 0)
                {
                    await RemoveTagAsync(_renderedTags[^1], restoreFocus: false);
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
        _suppressDropdownOnNextFocus = false;

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

        if (_suppressDropdownOnNextFocus)
        {
            // Restoring focus to the input after removing the sole chip: do NOT reopen the listbox, or the just-removed
            // tag surfaces as the active suggestion and Enter re-adds it.
            _suppressDropdownOnNextFocus = false;

            return;
        }

        _isDropdownOpen = true;
        RecomputeFilteredSuggestions();
    }

    private async Task OnSuggestionMouseDownAsync(string suggestion)
    {
        await CommitSuggestionAsync(suggestion);
        await _inputRef.FocusAsync();
    }

    private string OptionId(int index) => $"{_listboxId}-opt-{index}";

    // Drops refs for chips no longer rendered (captures never re-run, so a stale entry throws on focus).
    private void PruneRemoveButtons()
    {
        if (_removeButtons.Count == 0) { return; }

        List<string> stale = [.. _removeButtons.Keys.Where(tag => !_renderedTags.Contains(tag))];

        foreach (var tag in stale) { _removeButtons.Remove(tag); }
    }

    private void RecomputeFilteredSuggestions()
    {
        var alreadySelected = new HashSet<string>(Value, StringComparer.OrdinalIgnoreCase);
        var typed = _inputText?.Trim() ?? string.Empty;

        _filteredSuggestions = [.. SuggestionSource
            .Where(s => !alreadySelected.Contains(s))
            .Where(s => typed.Length == 0 || s.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)];
    }

    private string RemoveTagAriaLabel(string tag) =>
        string.Format(CultureInfo.CurrentCulture, RemoveTagAriaLabelFormat, tag);

    private async Task RemoveTagAsync(string tag, bool restoreFocus)
    {
        var tagsBefore = Value;
        var updated = tagsBefore.RemoveAll(existing => string.Equals(existing, tag, StringComparison.Ordinal));

        if (updated.Count == tagsBefore.Count) { return; }

        if (restoreFocus) { ArmNeighborFocus(tag); }

        Value = updated;
        await ValueChanged.InvokeAsync(Value);

        RecomputeFilteredSuggestions();
        _activeOptionIndex = -1;
    }

    // Removal is deferred behind the host's async persist, so by the time it propagates the user may have moved focus:
    // restore only if focus fell to the document root, and fail closed if the module is unavailable. Returns whether
    // focus moved.
    private async ValueTask<bool> RestoreFocusIfOrphanedAsync(ElementReference target)
    {
        try
        {
            _focusModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>(
                "import",
                "./_content/EventLogExpert.UI/Common/focusGuard.js");

            return await _focusModule.InvokeAsync<bool>("focusIfNotElsewhere", target, true);
        }
        catch (JSDisconnectedException) { return false; }
        catch (JSException) { return false; }
        catch (ObjectDisposedException) { return false; }
        catch (TaskCanceledException) { return false; }
    }

    // Focus the first recorded fallback chip still rendered; overlapping async removals can delete the nearest recorded
    // candidate before this consumes, so walk the ordered list. If none survive, focus the input instead of stranding.
    private async Task RestoreNeighborOrInputFocusAsync(IReadOnlyList<string>? targetTags)
    {
        if (targetTags is not null)
        {
            foreach (var tag in targetTags)
            {
                if (_removeButtons.TryGetValue(tag, out var button) && button is not null)
                {
                    await RestoreFocusIfOrphanedAsync(button.Element);

                    return;
                }
            }
        }

        // Set the flag BEFORE the await so the real focusin reaches OnInputFocus (which consumes it) first; if the guard
        // declines or is unavailable the input is never focused, so clear it to avoid stranding it.
        _suppressDropdownOnNextFocus = true;

        if (!await RestoreFocusIfOrphanedAsync(_inputRef))
        {
            _suppressDropdownOnNextFocus = false;
        }
    }
}
