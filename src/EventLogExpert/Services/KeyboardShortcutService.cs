// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Services;
using Microsoft.JSInterop;

namespace EventLogExpert.Services;

/// <summary>
///     Hosts the .NET side of the JS keydown bridge. Webview JS calls <see cref="HandleShortcutAsync" /> for any
///     Ctrl-modified key it considers menu-relevant after the bridge has already synchronously suppressed the
///     browser default; this class then decides whether to run the corresponding action.
/// </summary>
public sealed class KeyboardShortcutService(
    IMenuActionService actions,
    IModalService modalService,
    ISettingsService settings) : IAsyncDisposable
{
    private readonly IMenuActionService _actions = actions;
    private readonly IModalService _modalService = modalService;
    private readonly ISettingsService _settings = settings;

    private IJSRuntime? _jsRuntime;
    private bool _registered;

    private DotNetObjectReference<KeyboardShortcutService>? _selfRef;

    public async ValueTask DisposeAsync() => await UnregisterAsync();

    public async ValueTask UnregisterAsync()
    {
        if (_selfRef is null) { return; }

        if (_jsRuntime is not null)
        {
            try
            {
                await _jsRuntime.InvokeVoidAsync("unregisterKeyboardShortcuts");
            }
            catch (JSDisconnectedException) { }
            catch (TaskCanceledException) { }
        }

        _selfRef.Dispose();
        _selfRef = null;
        _registered = false;
    }

    public async Task EnsureRegisteredAsync(IJSRuntime jsRuntime)
    {
        // The JS bridge is idempotent on re-register and refreshes its DotNetObjectReference, so we
        // always invoke it. This keeps shortcuts working after WebView reloads / circuit restarts /
        // hot reload, even though this service is a DI singleton whose _registered flag would
        // otherwise cause us to skip re-registration when the JS side has lost its listener.
        var previousJsRuntime = _jsRuntime;
        var previousSelfRef = _selfRef;
        var previousRegistered = _registered;
        var newSelfRef = DotNetObjectReference.Create(this);

        try
        {
            await jsRuntime.InvokeVoidAsync("registerKeyboardShortcuts", newSelfRef);

            _jsRuntime = jsRuntime;
            _selfRef = newSelfRef;
            _registered = true;

            if (previousSelfRef is not null && !ReferenceEquals(previousSelfRef, newSelfRef))
            {
                previousSelfRef.Dispose();
            }
        }
        catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException)
        {
            newSelfRef.Dispose();
            _jsRuntime = previousJsRuntime;
            _selfRef = previousSelfRef;
            _registered = previousRegistered;
        }
    }

    /// <summary>
    ///     Runs the requested shortcut action when applicable. The JS bridge has already synchronously
    ///     called <c>preventDefault</c>/<c>stopPropagation</c> in capture phase before invoking this method,
    ///     so the return value would be ignored — this method is intentionally <see cref="Task" /> rather
    ///     than <c>Task&lt;bool&gt;</c>. When a modal is active, the action is skipped (no-op) so modal
    ///     keybindings stay isolated; the browser default has still been suppressed by the bridge.
    /// </summary>
    [JSInvokable]
    public async Task HandleShortcutAsync(string code, bool ctrl, bool alt, bool shift, bool meta)
    {
        if (!ctrl || alt || shift || meta) { return; }

        // Modal-gating happens here, not in JS, so a misbehaving (or stale) bridge can't bypass it.
        if (_modalService.ActiveModalType is not null) { return; }

        switch (code)
        {
            case "KeyO":
                await _actions.OpenFileAsync(false);
                return;

            case "KeyH":
                _actions.ToggleShowAllEvents();
                return;

            case "KeyC":
                await _actions.CopySelectedAsync(_settings.CopyType);
                return;
        }
    }
}
