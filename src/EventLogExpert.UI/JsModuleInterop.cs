// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.JSInterop;

namespace EventLogExpert.UI;

/// <summary>
///     Shared helper for safely disposing the collocated JavaScript ES modules that UI components and services own.
///     Centralizes the canonical teardown catch set in one place so every <see cref="IJSObjectReference" /> owner disposes
///     consistently as the app evolves.
/// </summary>
internal static class JsModuleInterop
{
    /// <summary>
    ///     Disposes a JavaScript module reference, swallowing the exceptions expected when the Blazor circuit (or MAUI
    ///     WebView) is torn down mid-teardown. When <paramref name="preDispose" /> is supplied it runs first — typically a
    ///     "detach"/"unregister" call — inside the same try, so a circuit-gone failure there skips the redundant dispose,
    ///     matching the single-try teardown the call sites used before this helper. Callers should null their module field
    ///     after awaiting to guard against double disposal.
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
}
