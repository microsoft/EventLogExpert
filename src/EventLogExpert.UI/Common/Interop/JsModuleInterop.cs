// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.JSInterop;

namespace EventLogExpert.UI.Common.Interop;

internal static class JsModuleInterop
{
    /// <summary>
    ///     Disposes the module, swallowing teardown-time exceptions; an optional <paramref name="preDispose" /> runs in
    ///     the same try (so its failure skips the dispose), and callers should null their field afterward.
    /// </summary>
    public static async ValueTask DisposeModuleSafelyAsync(
        IJSObjectReference? module,
        Func<IJSObjectReference, ValueTask>? preDispose = null)
    {
        if (module is null) { return; }

        try
        {
            if (preDispose is not null) { await preDispose(module); }

            await module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
        catch (ObjectDisposedException) { }
        catch (TaskCanceledException) { }
    }

    public static async ValueTask DisposeModuleStrictAsync(IJSObjectReference? module)
    {
        if (module is null) { return; }

        try
        {
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
        catch (OperationCanceledException) { }
    }

    public static ValueTask<IJSObjectReference> ImportModuleAsync(IJSRuntime runtime, string modulePath)
    {
        var failureContext = $"Failed to import JavaScript module '{modulePath}'";

        try
        {
            return AddFailureContextAsync(runtime.InvokeAsync<IJSObjectReference>("import", modulePath), failureContext);
        }
        catch (JSException exception)
        {
            throw CreateFailureContextException(failureContext, exception);
        }
    }

    public static ValueTask<TValue> InvokeAsync<TValue>(
        IJSObjectReference module,
        string identifier,
        params object?[]? arguments)
    {
        var failureContext = $"JavaScript invocation '{identifier}' failed";

        try
        {
            return AddFailureContextAsync(module.InvokeAsync<TValue>(identifier, arguments), failureContext);
        }
        catch (JSException exception)
        {
            throw CreateFailureContextException(failureContext, exception);
        }
    }

    public static async ValueTask InvokeVoidAsync(
        IJSObjectReference module,
        string identifier,
        params object?[]? arguments)
    {
        var failureContext = $"JavaScript invocation '{identifier}' failed";

        try
        {
            await module.InvokeVoidAsync(identifier, arguments);
        }
        catch (JSException exception)
        {
            throw CreateFailureContextException(failureContext, exception);
        }
    }

    private static async ValueTask<TValue> AddFailureContextAsync<TValue>(ValueTask<TValue> invocation, string failureContext)
    {
        try
        {
            return await invocation;
        }
        catch (JSException exception)
        {
            throw CreateFailureContextException(failureContext, exception);
        }
    }

    private static JSException CreateFailureContextException(string failureContext, JSException exception)
    {
        return new JSException($"{failureContext}: {exception.Message}", exception);
    }
}
