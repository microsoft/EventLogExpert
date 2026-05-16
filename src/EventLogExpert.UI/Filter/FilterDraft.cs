// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering;
using EventLogExpert.Filtering.Persistence;
using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.UI.Filter;

public sealed class FilterDraft
{
    public HighlightColor Color { get; set; } = HighlightColor.None;

    public FilterConditionDraft Comparison { get; set; } = new();

    public string ComparisonText { get; set; } = string.Empty;

    /// <summary>
    ///     <see langword="true" /> when the primary <see cref="Comparison" /> has user-supplied input or any sub-filter
    ///     exists. Used by the row's mode-switch + Basic-mode save validation as the "structure carries data" signal —
    ///     <see cref="FilterConditionDraft.Property" /> defaulting to the first enum value (the dropdown's initial
    ///     selection) does NOT count as meaningful input on its own.
    /// </summary>
    public bool HasMeaningfulStructure =>
        !string.IsNullOrWhiteSpace(Comparison.Value) ||
        Comparison.Values.Count > 0 ||
        SubFilters.Count > 0;

    public FilterId Id { get; init; } = FilterId.Create();

    public bool IsEnabled { get; set; }

    public bool IsExcluded { get; set; }

    /// <summary>
    ///     Authoring mode for this draft. Drives which editor body the row renders and is persisted on save through to
    ///     <see cref="SavedFilter.Mode" /> so re-edit reopens on the same surface.
    /// </summary>
    public FilterMode Mode { get; set; } = FilterMode.Advanced;

    public List<SubFilterDraft> SubFilters { get; set; } = [];

    /// <summary>
    ///     Hydrates a draft from a persisted <see cref="SavedFilter" />. <see cref="Mode" /> propagates verbatim;
    ///     structured fields are hydrated only when <see cref="Mode" /> is <see cref="FilterMode.Basic" /> (Advanced and
    ///     Cached modes always reopen with empty structure even if the persisted record carries a stale
    ///     <see cref="SavedFilter.BasicFilter" />).
    /// </summary>
    public static FilterDraft FromSavedFilter(SavedFilter filter)
    {
        var draft = new FilterDraft
        {
            Id = filter.Id,
            Color = filter.Color,
            ComparisonText = filter.ComparisonText,
            IsEnabled = filter.IsEnabled,
            IsExcluded = filter.IsExcluded,
            Mode = filter.Mode
        };

        if (filter.Mode == FilterMode.Basic && filter.BasicFilter is { } basicFilter)
        {
            draft.HydrateStructure(basicFilter);
        }

        return draft;
    }

    /// <summary>
    ///     Replaces <see cref="Comparison" /> and <see cref="SubFilters" /> with structure decomposed (or otherwise
    ///     derived) from <paramref name="basicFilter" />. Used by mode-switch when adopting decomposed Advanced/Cached
    ///     text as Basic structure.
    /// </summary>
    public void HydrateStructure(BasicFilter basicFilter)
    {
        Comparison = FilterConditionDraft.FromCondition(basicFilter.Comparison);
        SubFilters = [.. basicFilter.SubFilters.Select(SubFilterDraftFromSubFilter)];
    }

    public BasicFilter ToBasicFilter() =>
        new(Comparison.ToCondition(), [.. SubFilters.Select(subFilter => subFilter.ToSubFilter())]);

    /// <summary>
    ///     Builds a <see cref="SavedFilter" /> from this draft per <see cref="Mode" />. Returns <see langword="false" />
    ///     with a populated <paramref name="error" /> when the draft cannot be saved (empty input, incomplete Basic
    ///     sub-filter, or compile failure on the resulting text). Pure (does not mutate the draft) so the caller can
    ///     surface the error inline and let the user repair.
    /// </summary>
    public bool TryBuildSavedFilter([NotNullWhen(true)] out SavedFilter? saved, out string error)
    {
        saved = null;
        error = string.Empty;

        string text;
        BasicFilter? basicFilter;

        if (Mode == FilterMode.Basic)
        {
            if (!HasMeaningfulStructure)
            {
                error = "Cannot save an empty filter";
                return false;
            }

            var draftBasic = ToBasicFilter();

            if (!BasicFilterFormatter.TryFormat(draftBasic, strictSubFilters: true, out var formatted))
            {
                error = "All sub-filters must be complete before saving.";
                return false;
            }

            text = formatted;
            basicFilter = draftBasic;
        }
        else
        {
            text = ComparisonText ?? string.Empty;
            basicFilter = null;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Cannot save an empty filter";
            return false;
        }

        if (!FilterCompiler.TryCompile(text, out var compiled, out var compileError))
        {
            error = compileError;
            return false;
        }

        saved = new SavedFilter
        {
            Id = Id,
            Color = Color,
            ComparisonText = text,
            Compiled = compiled,
            BasicFilter = basicFilter,
            Mode = Mode,
            IsEnabled = true,
            IsExcluded = IsExcluded
        };

        return true;
    }

    /// <summary>
    ///     Returns <see langword="true" /> when switching to <paramref name="target" /> would discard user input the
    ///     current mode carries. Pure predicate so callers can decide whether to prompt for confirmation before mutating
    ///     state via <see cref="ApplyModeSwitch" />.
    /// </summary>
    public bool WouldLoseDataSwitchingTo(FilterMode target)
    {
        if (Mode == target) { return false; }

        switch (target)
        {
            case FilterMode.Cached:
                // Cached can't accept arbitrary text — switching from Basic with structure OR from
                // Basic/Advanced with a non-empty expression discards the user's input.
                return Mode == FilterMode.Basic
                    ? HasMeaningfulStructure || !string.IsNullOrEmpty(ComparisonText)
                    : !string.IsNullOrEmpty(ComparisonText);
            case FilterMode.Basic:
                if (Mode == FilterMode.Advanced || Mode == FilterMode.Cached)
                {
                    return !string.IsNullOrEmpty(ComparisonText) &&
                        !BasicFilterDecomposer.TryDecompose(ComparisonText, out _);
                }

                return false;
            case FilterMode.Advanced:
                if (Mode == FilterMode.Basic)
                {
                    // Basic→Advanced is loss-free unless the structure carries data the strict formatter refuses
                    // (incomplete sub-filter etc.). Empty structure with degraded persisted ComparisonText is also
                    // loss-free here because ApplyModeSwitch preserves the text.
                    return HasMeaningfulStructure &&
                        !BasicFilterFormatter.TryFormat(ToBasicFilter(), strictSubFilters: true, out _);
                }

                return false;
            default:
                return false;
        }
    }

    /// <summary>
    ///     Mutates the draft for a mode change. Caller is responsible for confirming with the user FIRST when
    ///     <see cref="WouldLoseDataSwitchingTo" /> reports data loss. No-op when <paramref name="target" /> equals the
    ///     current <see cref="Mode" />.
    /// </summary>
    public void ApplyModeSwitch(FilterMode target)
    {
        if (Mode == target) { return; }

        switch (target)
        {
            case FilterMode.Cached:
                ComparisonText = string.Empty;
                ClearStructure();
                break;
            case FilterMode.Basic:
                if (!string.IsNullOrEmpty(ComparisonText) &&
                    BasicFilterDecomposer.TryDecompose(ComparisonText, out var decomposed) &&
                    decomposed is { } basicFilter)
                {
                    HydrateStructure(basicFilter);
                }
                else
                {
                    ComparisonText = string.Empty;
                    ClearStructure();
                }

                break;
            case FilterMode.Advanced:
                if (Mode == FilterMode.Basic)
                {
                    // Strict first so a clean structured shape produces clean text. Lenient fallback covers the
                    // user-confirmed data-loss path (the row only reaches the lossy branch after
                    // WouldLoseDataSwitchingTo returned true and the user accepted the prompt). When neither
                    // formatter produces text — empty structure or all sub-filters unprintable — fall back to the
                    // existing ComparisonText (preserves the degraded-persisted-Basic recovery surface).
                    if (HasMeaningfulStructure
                        && BasicFilterFormatter.TryFormat(ToBasicFilter(), strictSubFilters: true, out var strict))
                    {
                        ComparisonText = strict;
                    }
                    else if (HasMeaningfulStructure
                        && BasicFilterFormatter.TryFormat(ToBasicFilter(), strictSubFilters: false, out var lenient)
                        && !string.IsNullOrEmpty(lenient))
                    {
                        ComparisonText = lenient;
                    }

                    // else: leave ComparisonText as-is so a Basic row that arrived with degraded structure
                    // (BasicFilter blob was unrecoverable but ComparisonText survived) still has its raw text
                    // exposed in the Advanced editor for manual repair.

                    ClearStructure();
                }
                else if (Mode == FilterMode.Cached)
                {
                    // Loss-free: text preserved verbatim, defensively clear structure (was unused in Cached).
                    ClearStructure();
                }

                break;
        }

        Mode = target;
    }

    private static SubFilterDraft SubFilterDraftFromSubFilter(SubFilter subFilter) =>
        new()
        {
            Condition = FilterConditionDraft.FromCondition(subFilter.Comparison),
            JoinWithAny = subFilter.JoinWithAny
        };

    private void ClearStructure()
    {
        Comparison = new FilterConditionDraft();
        SubFilters.Clear();
    }
}
