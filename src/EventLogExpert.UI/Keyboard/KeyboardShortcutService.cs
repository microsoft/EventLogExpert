// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.Common.Interop;
using EventLogExpert.UI.LogTable.Find;
using EventLogExpert.UI.Modal;
using Microsoft.JSInterop;
using System.Runtime.ExceptionServices;

namespace EventLogExpert.UI.Keyboard;

public sealed class KeyboardShortcutService(
    IMenuActionService actions,
    IModalCoordinator modalCoordinator,
    IFindCoordinator findCoordinator,
    ISettingsService settings) : IAsyncDisposable
{
    private readonly IMenuActionService _actions = actions;
    private readonly IFindCoordinator _findCoordinator = findCoordinator;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IModalCoordinator _modalCoordinator = modalCoordinator;
    private readonly ISettingsService _settings = settings;

    private AttachedRegistration? _current;
    private bool _disposed;
    private long _leaseSequence;

    public KeyboardShortcutLease CreateLease() => new(Interlocked.Increment(ref _leaseSequence));

    public async ValueTask DisposeAsync()
    {
        List<Exception> failures = [];

        await _gate.WaitAsync();

        try
        {
            _disposed = true;

            var current = _current;
            _current = null;

            if (current is not null)
            {
                await DetachAsync(current, failures);
            }
        }
        finally
        {
            _gate.Release();
        }

        ThrowIfFailed(failures);
    }

    public async Task EnsureRegisteredAsync(KeyboardShortcutLease lease, IJSRuntime jsRuntime)
    {
        IJSObjectReference module;

        try
        {
            module = await jsRuntime.InvokeAsync<IJSObjectReference>("import", "./_content/EventLogExpert.UI/Keyboard/Keyboard.js");
        }
        catch (Exception ex) when (IsExpectedJavaScriptFailure(ex))
        {
            return;
        }

        List<Exception> failures = [];
        bool declined;

        await _gate.WaitAsync();

        try
        {
            declined = _disposed || lease.IsReleased || lease.Sequence < Volatile.Read(ref _leaseSequence);

            if (!declined)
            {
                await RegisterAsync(lease, module, failures);
            }
        }
        finally
        {
            _gate.Release();
        }

        if (declined)
        {
            await DisposeModuleAsync(module, failures);
        }

        ThrowIfFailed(failures);
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

    public async ValueTask UnregisterAsync(KeyboardShortcutLease lease)
    {
        lease.Release();

        List<Exception> failures = [];

        await _gate.WaitAsync();

        try
        {
            if (_current is { } current && ReferenceEquals(current.Lease, lease))
            {
                _current = null;

                await DetachAsync(current, failures);
            }
        }
        finally
        {
            _gate.Release();
        }

        ThrowIfFailed(failures);
    }

    private static async ValueTask DetachAsync(AttachedRegistration registration, List<Exception> failures)
    {
        try
        {
            await registration.Module.InvokeVoidAsync("unregisterKeyboardShortcuts");
        }
        catch (Exception ex)
        {
            RecordTeardownFailure(failures, ex);
        }

        await RetireAsync(registration, failures);
    }

    private static async ValueTask DisposeModuleAsync(IJSObjectReference module, List<Exception> failures)
    {
        try
        {
            await JsModuleInterop.DisposeModuleStrictAsync(module);
        }
        catch (Exception ex)
        {
            failures.Add(ex);
        }
    }

    private static bool IsExpectedJavaScriptFailure(Exception exception) =>
        exception is JSDisconnectedException or JSException or OperationCanceledException;

    private static void RecordTeardownFailure(List<Exception> failures, Exception exception)
    {
        if (exception is not (JSDisconnectedException or OperationCanceledException or ObjectDisposedException))
        {
            failures.Add(exception);
        }
    }

    private static async ValueTask RetireAsync(AttachedRegistration registration, List<Exception> failures)
    {
        try
        {
            registration.Reference.Dispose();
        }
        catch (Exception ex)
        {
            RecordTeardownFailure(failures, ex);
        }

        await DisposeModuleAsync(registration.Module, failures);
    }

    private static void ThrowIfFailed(List<Exception> failures)
    {
        if (failures.Count == 0) { return; }

        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        throw new AggregateException(failures);
    }

    private async ValueTask RegisterAsync(KeyboardShortcutLease lease, IJSObjectReference module, List<Exception> failures)
    {
        AttachedRegistration candidate = new(module, DotNetObjectReference.Create(this), lease);
        Exception? registerFailure = null;

        try
        {
            await module.InvokeVoidAsync("registerKeyboardShortcuts", candidate.Reference);
        }
        catch (Exception ex)
        {
            registerFailure = ex;
        }

        var previous = _current;

        if (registerFailure is not null || lease.IsReleased)
        {
            _current = null;

            if (registerFailure is not null && !IsExpectedJavaScriptFailure(registerFailure))
            {
                failures.Add(registerFailure);
            }

            await DetachAsync(candidate, failures);

            if (previous is not null)
            {
                await RetireAsync(previous, failures);
            }

            return;
        }

        _current = candidate;

        if (previous is not null)
        {
            await RetireAsync(previous, failures);
        }
    }

    private sealed record AttachedRegistration(
        IJSObjectReference Module,
        DotNetObjectReference<KeyboardShortcutService> Reference,
        KeyboardShortcutLease Lease);
}
