// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.DebugLog;
using EventLogExpert.UI.Focus;
using EventLogExpert.UI.Inputs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.UI.DebugLog;

public sealed partial class DebugLogFilterRow
{
    private const string UncategorizedLabel = "(Uncategorized)";

    private static readonly IReadOnlyList<string> s_levelValues =
    [
        nameof(LogLevel.Trace),
        nameof(LogLevel.Debug),
        nameof(LogLevel.Information),
        nameof(LogLevel.Warning),
        nameof(LogLevel.Error),
        nameof(LogLevel.Critical),
    ];

    private static readonly IReadOnlyList<string> s_processValues =
    [
        nameof(ProcessOrigin.InProcess),
        nameof(ProcessOrigin.ElevatedHelper),
    ];

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

    // Process is intentionally single-select only: ProcessOrigin has two values, so multi-select adds no
    // expressive power over single Equals plus the Include/Exclude toggle.
    private bool FieldSupportsMany => Draft?.Field is DebugLogFilterField.Level or DebugLogFilterField.Category;

    private bool FieldSupportsText => Draft?.Field == DebugLogFilterField.Message;

    private string? SingleValue => Draft is { Values.Count: > 0 } draft ? draft.Values[0] : null;

    internal ValueTask FocusChipEditButtonAsync() =>
        _chipEditButton is { } button ? ElementFocus.SafelyAsync(button.Element) : ValueTask.CompletedTask;

    internal ValueTask FocusEditorFirstControlAsync() =>
        _editorFirstControl is { } button ? ElementFocus.SafelyAsync(button.Element) : ValueTask.CompletedTask;

    // Collapsed-chip label for the APPLIED filter, e.g. "Level in Warning, Error" or "Message contains foo"; the
    // include/exclude and enabled state are conveyed by the toggle icons, not the text.
    private static string FilterSummary(DebugLogFilter filter)
    {
        string operatorLabel = (filter.Operator, filter.MatchMode) switch
        {
            (ComparisonOperator.Equals, MatchMode.Many) => "in",
            (ComparisonOperator.Equals, _) => "==",
            (ComparisonOperator.Contains, _) => "contains",
            (ComparisonOperator.NotEqual, _) => "!=",
            (ComparisonOperator.NotContains, _) => "doesn't contain",
            _ => "?"
        };

        string valueLabel = !filter.IsComplete
            ? "?"
            : string.Join(", ", filter.Values.Select(value => FormatValue(filter.Field, value)));

        return $"{filter.Field} {operatorLabel} {valueLabel}";
    }

    private static string FormatValue(DebugLogFilterField field, string? value) => field switch
    {
        DebugLogFilterField.Process => value switch
        {
            nameof(ProcessOrigin.InProcess) => "In-process",
            nameof(ProcessOrigin.ElevatedHelper) => "Elevated helper",
            _ => value ?? string.Empty
        },
        // Null is "no selection" (blank header); the empty string is the real "(Uncategorized)" category value.
        DebugLogFilterField.Category => value is null ? string.Empty : value.Length == 0 ? UncategorizedLabel : value,
        _ => value ?? string.Empty
    };

    private IReadOnlyList<string> CategoryOptions()
    {
        var options = new SortedSet<string>(AvailableCategories, StringComparer.Ordinal);

        foreach (var value in Draft?.Values ?? []) { options.Add(value); }

        return [.. options];
    }

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
        DebugLogFilterField.Process => s_processValues,
        DebugLogFilterField.Category => CategoryOptions(),
        _ => []
    };
}
