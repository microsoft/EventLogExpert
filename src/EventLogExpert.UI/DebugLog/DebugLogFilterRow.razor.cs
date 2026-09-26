// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Localization;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.DebugLog;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.Focus;
using EventLogExpert.UI.Inputs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.UI.DebugLog;

public sealed partial class DebugLogFilterRow
{
    private static readonly IReadOnlyList<string> s_levelValues =
    [
        nameof(LogLevel.Trace),
        nameof(LogLevel.Debug),
        nameof(LogLevel.Information),
        nameof(LogLevel.Warning),
        nameof(LogLevel.Error),
        nameof(LogLevel.Critical),
    ];

    private static readonly IReadOnlyList<string> s_processOriginValues =
    [
        nameof(ProcessOrigin.InProcess),
        nameof(ProcessOrigin.ElevatedHelper),
    ];

    // Per-row unique id so each open draft editor's disabled-save hint (aria-describedby target) stays unique
    // across the multiple filter rows rendered in the modal.
    private readonly string _saveHintId = ComponentId.NewUnique("debug-filter-save-hint").Value;

    private ChromelessButton? _chipEditButton;
    private Button? _editorFirstControl;

    // The applied, immutable filter shown in the collapsed chip; null for a never-saved new row.
    [Parameter] public DebugLogFilter? Applied { get; set; }

    [Parameter] public IReadOnlyList<string> AvailableCategories { get; set; } = [];

    // The draft copy shown in the editor; null when the row is collapsed to a chip.
    [Parameter] public DebugLogFilterDraft? Draft { get; set; }

    [Parameter] public EventCallback OnCancel { get; set; }

    [Parameter] public EventCallback OnChanged { get; set; }

    [Parameter] public EventCallback OnEdit { get; set; }

    [Parameter] public EventCallback OnEnableToggled { get; set; }

    [Parameter] public EventCallback OnExcludeToggled { get; set; }

    [Parameter] public EventCallback OnRemove { get; set; }

    [Parameter] public EventCallback OnSave { get; set; }

    // ProcessOrigin is intentionally single-select only: it has two values, so multi-select adds no
    // expressive power over single Equals plus the Include/Exclude toggle.
    private bool FieldSupportsMany => Draft?.Field is DebugLogFilterField.Level or DebugLogFilterField.Category;

    private bool FieldSupportsText => Draft?.Field == DebugLogFilterField.Message;

    [Inject] private IStringLocalizer<SharedResource> Localizer { get; init; } = null!;

    private string? SingleValue => Draft is { Values.Count: > 0 } draft ? draft.Values[0] : null;

    internal ValueTask FocusChipEditButtonAsync() =>
        _chipEditButton is { } button ? ElementFocus.SafelyAsync(button.Element) : ValueTask.CompletedTask;

    internal ValueTask FocusEditorFirstControlAsync() =>
        _editorFirstControl is { } button ? ElementFocus.SafelyAsync(button.Element) : ValueTask.CompletedTask;

    private IReadOnlyList<string> CategoryOptions()
    {
        var options = new SortedSet<string>(AvailableCategories, StringComparer.Ordinal);

        foreach (var value in Draft?.Values ?? []) { options.Add(value); }

        return [.. options];
    }

    // Localized filter-property token for the summary chip + field dropdown; literal keys keep the orphan-key scan happy.
    private string FieldLabel(DebugLogFilterField field) => field switch
    {
        DebugLogFilterField.Message => Localizer["DebugLog_Field_Message"].Value,
        DebugLogFilterField.Level => Localizer["DebugLog_Field_Level"].Value,
        DebugLogFilterField.Category => Localizer["DebugLog_Field_Category"].Value,
        DebugLogFilterField.ProcessOrigin => Localizer["DebugLog_Field_ProcessOrigin"].Value,
        _ => field.ToString()
    };

    // Collapsed-chip label for the APPLIED filter, e.g. "Level in Warning, Error" or "Message contains foo"; the
    // include/exclude and enabled state are conveyed by the toggle icons, not the text. Reuses the shared filter
    // predicate-summary template + compact operator labels so the localized chip stays terse and byte-identical.
    private string FilterSummary(DebugLogFilter filter)
    {
        string operatorLabel = (filter.Operator, filter.MatchMode) switch
        {
            (ComparisonOperator.Equals, MatchMode.Many) => Localizer["FilterEditor_PredicateSummary_Operator_In"].Value,
            (ComparisonOperator.Equals, _) => "==",
            (ComparisonOperator.Contains, _) => Localizer["FilterEditor_PredicateSummary_Operator_Contains"].Value,
            (ComparisonOperator.NotEqual, _) => "!=",
            (ComparisonOperator.NotContains, _) => Localizer["FilterEditor_PredicateSummary_Operator_NotContains"].Value,
            _ => "?"
        };

        string valueLabel = !filter.IsComplete
            ? "?"
            : string.Join(", ", filter.Values.Select(value => FormatValue(filter.Field, value)));

        return Localizer["FilterEditor_PredicateSummary", FieldLabel(filter.Field), operatorLabel, valueLabel];
    }

    private string FormatValue(DebugLogFilterField field, string? value) => field switch
    {
        // Reuses Settings_LogLevel_* so the level display localizes; byte-identical in English. Empty/null render
        // blank (no bogus "Settings_LogLevel_" key-echo), matching the prior default arm's value ?? string.Empty.
        DebugLogFilterField.Level => string.IsNullOrEmpty(value) ? string.Empty : Localizer[$"Settings_LogLevel_{value}"].Value,
        DebugLogFilterField.ProcessOrigin => value switch
        {
            nameof(ProcessOrigin.InProcess) => Localizer["DebugLog_ProcessOrigin_InProcess"].Value,
            nameof(ProcessOrigin.ElevatedHelper) => Localizer["DebugLog_ProcessOrigin_ElevatedHelper"].Value,
            _ => value ?? string.Empty
        },
        // Null is "no selection" (blank header); the empty string is the real "(Uncategorized)" category value.
        DebugLogFilterField.Category => value is null ? string.Empty : value.Length == 0 ? Localizer["DebugLog_Category_Uncategorized"].Value : value,
        _ => value ?? string.Empty
    };

    private async Task OnEditorExcludeToggled()
    {
        if (Draft is not { } draft) { return; }

        draft.IsExcluded = !draft.IsExcluded;

        await OnChanged.InvokeAsync();
    }

    private async Task OnFieldChanged(DebugLogFilterField field)
    {
        if (Draft is not { } draft) { return; }

        draft.Field = field;
        draft.Operator = field == DebugLogFilterField.Message ? ComparisonOperator.Contains : ComparisonOperator.Equals;
        draft.MatchMode = MatchMode.Single;
        draft.Values = [];

        await OnChanged.InvokeAsync();
    }

    private async Task OnOperatorChanged((ComparisonOperator Op, MatchMode Mode) value)
    {
        if (Draft is not { } draft) { return; }

        draft.Operator = value.Op;
        draft.MatchMode = value.Mode;

        if (value.Mode == MatchMode.Single && draft.Values.Count > 1)
        {
            draft.Values = [draft.Values[0]];
        }

        await OnChanged.InvokeAsync();
    }

    private async Task OnSingleValueChanged(string? value)
    {
        if (Draft is not { } draft) { return; }

        // Clear only on the ValueSelect's null clear-item; the empty string is the real "(Uncategorized)" category value.
        draft.Values = value is null ? [] : [value];

        await OnChanged.InvokeAsync();
    }

    private async Task OnValuesChanged(List<string> values)
    {
        if (Draft is not { } draft) { return; }

        draft.Values = values;

        await OnChanged.InvokeAsync();
    }

    private string ValueLabel(string? value) => FormatValue(Draft?.Field ?? DebugLogFilterField.Message, value);

    private IReadOnlyList<string> ValueOptions() => Draft?.Field switch
    {
        DebugLogFilterField.Level => s_levelValues,
        DebugLogFilterField.ProcessOrigin => s_processOriginValues,
        DebugLogFilterField.Category => CategoryOptions(),
        _ => []
    };
}
