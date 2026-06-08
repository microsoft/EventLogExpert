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
}
