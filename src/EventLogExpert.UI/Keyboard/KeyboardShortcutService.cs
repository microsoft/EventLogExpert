// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.Common.Interop;
using EventLogExpert.UI.LogTable.Find;
using EventLogExpert.UI.Modal;
using Microsoft.JSInterop;

namespace EventLogExpert.UI.Keyboard;

public sealed class KeyboardShortcutService(
    IMenuActionService actions,
    IModalCoordinator modalCoordinator,
    IFindCoordinator findCoordinator,
    ISettingsService settings) : IAsyncDisposable
{
    private readonly IMenuActionService _actions = actions;
    private readonly IFindCoordinator _findCoordinator = findCoordinator;
    private readonly IModalCoordinator _modalCoordinator = modalCoordinator;
    private readonly ISettingsService _settings = settings;

    private IJSObjectReference? _keyboardModule;
    private DotNetObjectReference<KeyboardShortcutService>? _selfRef;

    public async ValueTask DisposeAsync()
    {
        await UnregisterAsync();

        if (_keyboardModule is not null)
        {
            await JsModuleInterop.DisposeModuleSafelyAsync(_keyboardModule);

            _keyboardModule = null;
        }
    }

    public async Task EnsureRegisteredAsync(IJSRuntime jsRuntime)
    {
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
                await JsModuleInterop.DisposeModuleSafelyAsync(newModule);
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
            await JsModuleInterop.DisposeModuleSafelyAsync(previousModule);
        }
    }

    [JSInvokable]
    public async Task HandleShortcutAsync(string code, bool ctrl, bool alt, bool shift, bool meta)
    {
        if (!ctrl || alt || shift || meta) { return; }

        if (_modalCoordinator.ActiveSession is not null) { return; }

        switch (code)
        {
            case "KeyF":
                _findCoordinator.RequestOpen();
                return;

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
            catch (JSDisconnectedException) { /* Circuit gone - listener already detached. */ }
            catch (TaskCanceledException) { /* Teardown cancellation; nothing to do. */ }
            catch (ObjectDisposedException) { }
        }

        _selfRef.Dispose();
        _selfRef = null;
    }
}
