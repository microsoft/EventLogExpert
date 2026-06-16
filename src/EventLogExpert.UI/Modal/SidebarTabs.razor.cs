// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Common;
using EventLogExpert.UI.Common.Interop;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace EventLogExpert.UI.Modal;

public sealed partial class SidebarTabs<TTab> : ComponentBase, IAsyncDisposable
    where TTab : struct, Enum
{
    private readonly Dictionary<TTab, ElementReference> _tabButtonRefs = new();
    private readonly string _tablistId = ComponentId.NewUnique().Value;

    private TTab? _pendingFocusTab;
    private IJSObjectReference? _tabKeyModule;
    private ElementReference _tablistRef;

    [Parameter] public TTab ActiveTab { get; set; }

    [Parameter] public EventCallback<TTab> ActiveTabChanged { get; set; }

    [Parameter] public Func<TTab, bool>? IsTabpanelFocusable { get; set; }

    [Parameter] public EventCallback<TTab> OnTabActivated { get; set; }

    [Parameter][EditorRequired] public RenderFragment<TTab> TabContent { get; set; } = null!;

    [Parameter] public string TablistAriaLabel { get; set; } = "Tabs";

    [Parameter][EditorRequired] public IReadOnlyList<(TTab Tab, string Label)> Tabs { get; set; } = [];

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    private string TablistId => _tablistId;

    public async ValueTask DisposeAsync()
    {
        await JsModuleInterop.DisposeModuleSafelyAsync(
            _tabKeyModule,
            module => module.InvokeVoidAsync("detach", _tablistRef));

        _tabKeyModule = null;
    }

    public async ValueTask<bool> FocusActiveTabAsync()
    {
        if (!_tabButtonRefs.TryGetValue(ActiveTab, out var elementRef)) { return false; }

        try
        {
            await elementRef.FocusAsync();

            return true;
        }
        catch
        {
            return false;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                _tabKeyModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
                    "import",
                    "./_content/EventLogExpert.UI/Modal/SidebarTabs.razor.js");

                await _tabKeyModule.InvokeVoidAsync("attach", _tablistRef);
            }
            catch (JSDisconnectedException) { }
            catch (JSException) { }
        }

        if (_pendingFocusTab is { } tab && _tabButtonRefs.TryGetValue(tab, out var elementRef))
        {
            _pendingFocusTab = null;

            try { await elementRef.FocusAsync(); }
            catch { }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    private int FindTabIndex(TTab tab)
    {
        for (int i = 0; i < Tabs.Count; i++)
        {
            if (Tabs[i].Tab.Equals(tab)) { return i; }
        }

        return -1;
    }

    private TTab NextTab(TTab current)
    {
        var index = FindTabIndex(current);

        if (index < 0) { return Tabs[0].Tab; }

        return index < Tabs.Count - 1 ? Tabs[index + 1].Tab : Tabs[0].Tab;
    }

    private async Task OnTabKeyDownAsync(KeyboardEventArgs e, TTab tab)
    {
        if (Tabs.Count == 0) { return; }

        TTab? target = e.Key switch
        {
            "ArrowDown" => NextTab(tab),
            "ArrowUp" => PrevTab(tab),
            "Home" => Tabs[0].Tab,
            "End" => Tabs[^1].Tab,
            _ => null,
        };

        if (target is not { } targetTab) { return; }

        await SetActiveTabAsync(targetTab);
    }

    private TTab PrevTab(TTab current)
    {
        var index = FindTabIndex(current);

        if (index < 0) { return Tabs[^1].Tab; }

        return index > 0 ? Tabs[index - 1].Tab : Tabs[^1].Tab;
    }

    private async Task SetActiveTabAsync(TTab tab)
    {
        if (tab.Equals(ActiveTab)) { return; }

        ActiveTab = tab;
        _pendingFocusTab = tab;

        await ActiveTabChanged.InvokeAsync(tab);
        await OnTabActivated.InvokeAsync(tab);
    }
}
