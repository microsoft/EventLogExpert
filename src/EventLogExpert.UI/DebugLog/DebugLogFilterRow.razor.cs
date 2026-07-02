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

    [Parameter] public IReadOnlyList<string> AvailableCategories { get; set; } = [];

    [Parameter] public DebugLogFilterDraft Draft { get; set; } = null!;

    [Parameter] public bool IsEditing { get; set; }

    [Parameter] public EventCallback OnChanged { get; set; }

    [Parameter] public EventCallback OnDone { get; set; }

    [Parameter] public EventCallback OnEdit { get; set; }

    [Parameter] public EventCallback OnRemove { get; set; }

    [Parameter] public EventCallback OnStaged { get; set; }

    // Process is intentionally single-select only: ProcessOrigin has two values, so multi-select adds no
    // expressive power over single Equals plus the Include/Exclude toggle.
    private bool FieldSupportsMany => Draft.Field is DebugLogFilterField.Level or DebugLogFilterField.Category;

    private bool FieldSupportsText => Draft.Field == DebugLogFilterField.Message;

    private string? SingleValue => Draft.Values.Count > 0 ? Draft.Values[0] : null;

    // Collapsed-chip label, e.g. "Level in Warning, Error" or "Message contains foo"; the include/exclude
    // state is conveyed by the slash-circle toggle icon, not the text.
    private string SummaryText
    {
        get
        {
            string operatorLabel = (Draft.Operator, Draft.MatchMode) switch
            {
                (ComparisonOperator.Equals, MatchMode.Many) => "in",
                (ComparisonOperator.Equals, _) => "==",
                (ComparisonOperator.Contains, _) => "contains",
                (ComparisonOperator.NotEqual, _) => "!=",
                (ComparisonOperator.NotContains, _) => "doesn't contain",
                _ => "?"
            };

            string valueLabel = !Draft.IsComplete
                ? "?"
                : string.Join(", ", Draft.Values.Select(ValueLabel));

            return $"{Draft.Field} {operatorLabel} {valueLabel}";
        }
    }

    internal ValueTask FocusChipEditButtonAsync() =>
        _chipEditButton is { } button ? ElementFocus.SafelyAsync(button.Element) : ValueTask.CompletedTask;

    internal ValueTask FocusEditorFirstControlAsync() =>
        _editorFirstControl is { } button ? ElementFocus.SafelyAsync(button.Element) : ValueTask.CompletedTask;

    private IReadOnlyList<string> CategoryOptions()
    {
        var options = new SortedSet<string>(AvailableCategories, StringComparer.Ordinal);

        foreach (var value in Draft.Values) { options.Add(value); }

        return [.. options];
    }

    // Chip exclude toggles a COMMITTED filter, so it applies live (OnChanged). Editor exclude STAGES like the other
    // editor controls (OnStaged) - it only takes effect when the user clicks Done.
    private async Task OnChipExcludedChanged(bool excluded)
    {
        Draft.IsExcluded = excluded;

        await OnChanged.InvokeAsync();
    }

    private Task OnDoneClick() => OnDone.InvokeAsync();

    private Task OnEditChip() => OnEdit.InvokeAsync();

    private async Task OnEditorExcludedChanged(bool excluded)
    {
        Draft.IsExcluded = excluded;

        await OnStaged.InvokeAsync();
    }

    private async Task OnFieldChanged(DebugLogFilterField field)
    {
        Draft.Field = field;
        Draft.Operator = field == DebugLogFilterField.Message ? ComparisonOperator.Contains : ComparisonOperator.Equals;
        Draft.MatchMode = MatchMode.Single;
        Draft.Values = [];

        await OnStaged.InvokeAsync();
    }

    private async Task OnOperatorChanged((ComparisonOperator Op, MatchMode Mode) value)
    {
        Draft.Operator = value.Op;
        Draft.MatchMode = value.Mode;

        if (value.Mode == MatchMode.Single && Draft.Values.Count > 1)
        {
            Draft.Values = [Draft.Values[0]];
        }

        await OnStaged.InvokeAsync();
    }

    private async Task OnSingleValueChanged(string? value)
    {
        // Clear only on the ValueSelect's null clear-item; the empty string is the real "(Uncategorized)" category value.
        Draft.Values = value is null ? [] : [value];

        await OnStaged.InvokeAsync();
    }

    private async Task OnValuesChanged(List<string> values)
    {
        Draft.Values = values;

        await OnStaged.InvokeAsync();
    }

    private string ValueLabel(string? value) => Draft.Field switch
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

    private IReadOnlyList<string> ValueOptions() => Draft.Field switch
    {
        DebugLogFilterField.Level => s_levelValues,
        DebugLogFilterField.Process => s_processValues,
        DebugLogFilterField.Category => CategoryOptions(),
        _ => []
    };
}
