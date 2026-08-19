// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.Stats;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.Common.Interop;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EventLogExpert.UI.LogTable.Stats;

public sealed partial class StatsDrawer : AppStateComponentBase
{
    private bool _disposed;
    private DotNetObjectReference<StatsDrawer>? _dotNetRef;
    private IJSObjectReference? _module;

    // The status-bar count chip is the only stats toggle, and it disappears when no log is active. Gate the drawer on
    // an open log too, so closing the last log auto-collapses the drawer instead of stranding it open with no control.
    private bool IsOpen => StatsVisibility.IsVisible && OpenLogsPresence.HasOpenLogs;

    [Inject] private IJSRuntime JsRuntime { get; init; } = null!;

    [Inject] private IOpenLogsPresenceSource OpenLogsPresence { get; init; } = null!;

    [Inject] private IStatsDrawerPreferencesProvider PreferencesProvider { get; init; } = null!;

    [Inject] private IStatsVisibilitySource StatsVisibility { get; init; } = null!;

    [JSInvokable]
    public void OnStatsDrawerHeightChanged(int height)
    {
        if (height > 0)
        {
            PreferencesProvider.StatsDrawerHeightPreference = height;
        }
    }

    protected override async ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;

            await JsModuleInterop.DisposeModuleSafelyAsync(
                _module,
                static module => module.InvokeVoidAsync("disposeStatsDrawerResizer"));

            _module = null;
            _dotNetRef?.Dispose();
            _dotNetRef = null;
        }

        await base.DisposeAsyncCore(disposing);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && !_disposed)
        {
            try
            {
                _dotNetRef = DotNetObjectReference.Create(this);
                _module = await JsRuntime.InvokeAsync<IJSObjectReference>(
                    "import", "./_content/EventLogExpert.UI/LogTable/Stats/StatsDrawer.razor.js");
                await _module.InvokeVoidAsync(
                    "enableStatsDrawerResizer", _dotNetRef, PreferencesProvider.StatsDrawerHeightPreference);
            }
            catch (JSDisconnectedException) { /* Circuit closed before the resizer attached. */ }
            catch (ObjectDisposedException) { /* Component torn down mid-init. */ }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override void OnInitialized()
    {
        ObserveSource(StatsVisibility);
        ObserveSource(OpenLogsPresence);
        base.OnInitialized();
    }
}
