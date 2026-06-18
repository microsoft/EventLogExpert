// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Scenarios.Catalog;
using EventLogExpert.UI.Common.Interop;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace EventLogExpert.UI.Dashboard;

public sealed partial class ScenarioBrowserPanel : IAsyncDisposable
{
    private readonly Dictionary<string, ElementReference> _optionRefs = new();

    private string? _pendingFocusId;
    private ElementReference _scenarioBrowserRootRef;
    private IJSObjectReference? _scrollSuppressorModule;

    [Parameter][EditorRequired] public string ElevationReasonId { get; set; } = string.Empty;

    [Parameter] public bool IsBusy { get; set; }

    [Parameter][EditorRequired] public Func<ScenarioDefinition, bool> IsFavored { get; set; } = static _ => false;

    [Parameter][EditorRequired] public Func<ScenarioDefinition, bool> IsScenarioDisabled { get; set; } = static _ => false;

    [Parameter] public EventCallback<ScenarioDefinition> OnLaunch { get; set; }

    [Parameter] public EventCallback<ScenarioDefinition> OnSelect { get; set; }

    [Parameter] public EventCallback<ScenarioDefinition> OnToggleFavorite { get; set; }

    [Parameter][EditorRequired] public IReadOnlyList<ScenarioDefinition> Scenarios { get; set; } = [];

    [Parameter] public ScenarioDefinition? Selected { get; set; }

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    public async ValueTask DisposeAsync()
    {
        await JsModuleInterop.DisposeModuleSafelyAsync(
            _scrollSuppressorModule,
            module => module.InvokeVoidAsync("release", _scenarioBrowserRootRef));

        _scrollSuppressorModule = null;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                _scrollSuppressorModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
                    "import",
                    "./_content/EventLogExpert.UI/Common/keyboardScrollSuppressor.js");

                await _scrollSuppressorModule.InvokeVoidAsync(
                    "suppress",
                    _scenarioBrowserRootRef,
                    new[] { new { selector = "[role='listbox']", keys = new[] { "ArrowUp", "ArrowDown", "Home", "End" } } });
            }
            catch (JSDisconnectedException) { }
            catch (JSException) { }
        }

        if (_pendingFocusId is { } id && _optionRefs.TryGetValue(id, out var elementRef))
        {
            _pendingFocusId = null;

            try { await elementRef.FocusAsync(); }
            catch { }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    private int IndexOf(ScenarioDefinition scenario)
    {
        for (int index = 0; index < Scenarios.Count; index++)
        {
            if (string.Equals(Scenarios[index].Id, scenario.Id, StringComparison.Ordinal)) { return index; }
        }

        return -1;
    }

    private bool IsSelected(ScenarioDefinition scenario) =>
        Selected is not null && string.Equals(Selected.Id, scenario.Id, StringComparison.Ordinal);

    private async Task OnListKeyDownAsync(KeyboardEventArgs e)
    {
        if (Scenarios.Count == 0) { return; }

        int current = Selected is null ? -1 : IndexOf(Selected);

        int target = e.Key switch
        {
            "ArrowDown" => current < 0 ? 0 : Math.Min(current + 1, Scenarios.Count - 1),
            "ArrowUp" => current <= 0 ? 0 : current - 1,
            "Home" => 0,
            "End" => Scenarios.Count - 1,
            _ => -1,
        };

        if (target < 0) { return; }

        ScenarioDefinition scenario = Scenarios[target];
        _pendingFocusId = scenario.Id;

        await OnSelect.InvokeAsync(scenario);
    }

    private string OptionClass(ScenarioDefinition scenario)
    {
        string classes = "scenario-browser__option";

        if (IsSelected(scenario)) { classes += " scenario-browser__option--selected"; }

        if (IsScenarioDisabled(scenario)) { classes += " scenario-browser__option--disabled"; }

        return classes;
    }
}
