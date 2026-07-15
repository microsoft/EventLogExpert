// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Common.Interop;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace EventLogExpert.UI.LogTable.Find;

public sealed partial class FindBar : IAsyncDisposable
{
    private ElementReference _input;
    private int _lastFocusSignal;
    private IJSObjectReference? _module;
    private bool _optionsOpen;

    [Parameter]
    public bool CaseSensitive { get; set; }

    [Parameter]
    public EventCallback<bool> CaseSensitiveChanged { get; set; }

    /// <summary>The 1-based position of the current match, or 0 when there is none.</summary>
    [Parameter]
    public int CurrentOrdinal { get; set; }

    [Parameter]
    public int FocusSignal { get; set; }

    [Parameter]
    public bool IsScanning { get; set; }

    [Parameter]
    public int MatchCount { get; set; }

    [Parameter]
    public EventCallback OnClose { get; set; }

    [Parameter]
    public EventCallback OnNext { get; set; }

    [Parameter]
    public EventCallback OnPrevious { get; set; }

    [Parameter]
    public string Query { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<string> QueryChanged { get; set; }

    [Parameter]
    public bool WholeWord { get; set; }

    [Parameter]
    public EventCallback<bool> WholeWordChanged { get; set; }

    [Parameter]
    public string WrapAnnouncement { get; set; } = string.Empty;

    private string CountText =>
        Query.Length == 0 ? string.Empty :
        IsScanning ? "Searching\u2026" :
        MatchCount == 0 ? "No results" :
        $"{CurrentOrdinal}/{MatchCount}";

    [Inject]
    private IJSRuntime JSRuntime { get; init; } = null!;

    private bool NavDisabled => IsScanning || MatchCount == 0;

    private bool OptionsActive => CaseSensitive || WholeWord;

    public async ValueTask DisposeAsync()
    {
        await JsModuleInterop.DisposeModuleSafelyAsync(
            _module,
            static module => module.InvokeVoidAsync("detachNativeFindSuppression"));

        _module = null;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        try
        {
            if (firstRender)
            {
                _module = await JSRuntime.InvokeAsync<IJSObjectReference>(
                    "import", "./_content/EventLogExpert.UI/LogTable/Find/FindBar.razor.js");
                await _module.InvokeVoidAsync("attachNativeFindSuppression");
            }

            if (FocusSignal != _lastFocusSignal && _module is not null)
            {
                _lastFocusSignal = FocusSignal;
                await _module.InvokeVoidAsync("focusAndSelect", _input);
            }
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
    }

    // Enter is bound to the input only (a focused nav button's Enter stays a single native click); F3/Esc bind to the bar root so they fire whichever child holds focus.
    private async Task HandleInputKeyDown(KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "Enter" when args.ShiftKey:
                await OnPrevious.InvokeAsync();
                return;

            case "Enter":
                await OnNext.InvokeAsync();
                return;
        }
    }

    private async Task HandleRootKeyDown(KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "F3" when args.ShiftKey:
                await OnPrevious.InvokeAsync();
                return;

            case "F3":
                await OnNext.InvokeAsync();
                return;

            case "Escape":
                await OnClose.InvokeAsync();
                return;
        }
    }

    private async Task OnCaseChanged(bool value) => await CaseSensitiveChanged.InvokeAsync(value);

    private async Task OnInput(ChangeEventArgs args) =>
        await QueryChanged.InvokeAsync(args.Value as string ?? string.Empty);

    private async Task OnWholeWordChanged(bool value) => await WholeWordChanged.InvokeAsync(value);

    private void ToggleOptions() => _optionsOpen = !_optionsOpen;
}
