// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.Runtime.Settings;
using Microsoft.JSInterop;

namespace EventLogExpert.UI.Keyboard;

/// <summary>
///     Hosts the .NET side of the JS keydown bridge. Webview JS calls <see cref="HandleShortcutAsync" /> for any
///     Ctrl-modified key it considers menu-relevant after the bridge has already synchronously suppressed the browser
///     default; this class then decides whether to run the corresponding action.
/// </summary>
public sealed class KeyboardShortcutService(
    IMenuActionService actions,
    IModalCoordinator modalCoordinator,
    ISettingsService settings) : IAsyncDisposable
{
    private readonly IMenuActionService _actions = actions;
    private readonly IModalCoordinator _modalCoordinator = modalCoordinator;
    private readonly ISettingsService _settings = settings;

    private IJSObjectReference? _keyboardModule;
    private DotNetObjectReference<KeyboardShortcutService>? _selfRef;

    public async ValueTask DisposeAsync()
    {
        await UnregisterAsync();

        if (_keyboardModule is not null)
        {
            await DisposeModuleSafelyAsync(_keyboardModule);
            _keyboardModule = null;
        }
    }

    public async Task EnsureRegisteredAsync(IJSRuntime jsRuntime)
    {
        // The JS bridge is idempotent on re-register and refreshes its DotNetObjectReference, so we
        // always invoke it. This keeps shortcuts working after WebView reloads / circuit restarts /
        // hot reload.
        var previousSelfRef = _selfRef;
        var previousModule = _keyboardModule;
        var newSelfRef = DotNetObjectReference.Create(this);
        IJSObjectReference? newModule = null;

        try
        {
            newModule = await jsRuntime.InvokeAsync<IJSObjectReference>("import", "./_content/EventLogExpert.UI/Keyboard/Keyboard.js");
            await newModule.InvokeVoidAsync("registerKeyboardShortcuts", newSelfRef);
        }
        catch (Exception ex) when (ex is JSDisconnectedException or JSException or TaskCanceledException)
        {
            newSelfRef.Dispose();

            if (newModule is not null)
            {
                await DisposeModuleSafelyAsync(newModule);
            }

            return;
        }

        _selfRef = newSelfRef;
        _keyboardModule = newModule;

        if (previousSelfRef is not null && !ReferenceEquals(previousSelfRef, newSelfRef))
        {
            previousSelfRef.Dispose();
        }

        if (previousModule is not null && !ReferenceEquals(previousModule, newModule))
        {
            await DisposeModuleSafelyAsync(previousModule);
        }
    }

    /// <summary>
    ///     Runs the requested shortcut action when applicable. The JS bridge has already synchronously called
    ///     <c>preventDefault</c>/<c>stopPropagation</c> in capture phase before invoking this method, so the return value
    ///     would be ignored — this method is intentionally <see cref="Task" /> rather than <c>Task&lt;bool&gt;</c>. When a
    ///     modal is active, the action is skipped (no-op) so modal keybindings stay isolated; the browser default has still
    ///     been suppressed by the bridge.
    /// </summary>
    [JSInvokable]
    public async Task HandleShortcutAsync(string code, bool ctrl, bool alt, bool shift, bool meta)
    {
        if (!ctrl || alt || shift || meta) { return; }

        // Modal-gating happens here, not in JS, so a misbehaving (or stale) bridge can't bypass it.
        if (_modalCoordinator.ActiveSession is not null) { return; }

        switch (code)
        {
            case "KeyO":
                await _actions.OpenFileAsync(false);
                return;

            case "KeyH":
                _actions.ToggleShowAllEvents();
                return;

            case "KeyC":
                await _actions.CopySelectedAsync(_settings.CopyFormat);
                return;
        }
    }

    public async ValueTask UnregisterAsync()
    {
        if (_selfRef is null) { return; }

        if (_keyboardModule is not null)
        {
            try
            {
                await _keyboardModule.InvokeVoidAsync("unregisterKeyboardShortcuts");
            }
            catch (JSDisconnectedException) { /* Circuit gone — listener already detached. */ }
            catch (TaskCanceledException) { /* Teardown cancellation; nothing to do. */ }
            catch (ObjectDisposedException) { }
        }

        _selfRef.Dispose();
        _selfRef = null;
    }

    private static async ValueTask DisposeModuleSafelyAsync(IJSObjectReference module)
    {
        try { await module.DisposeAsync(); }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
        catch (ObjectDisposedException) { }
        catch (TaskCanceledException) { }
    }
}
