// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EventLogExpert.UI.Focus;

// Best-effort focus helpers: swallow the transient interop failures a restore can hit (teardown, circuit loss,
// cancellation) plus InvalidOperationException from a default/never-captured ElementReference.
internal static class ElementFocus
{
    public static async ValueTask SafelyAsync(ElementReference target, bool preventScroll = false)
    {
        try
        {
            await target.FocusAsync(preventScroll);
        }
        catch (ObjectDisposedException) { }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
        catch (TaskCanceledException) { }
        catch (InvalidOperationException) { }
    }

    public static async ValueTask<bool> TrySafelyAsync(ElementReference target, bool preventScroll = false)
    {
        try
        {
            await target.FocusAsync(preventScroll);

            return true;
        }
        catch (ObjectDisposedException) { return false; }
        catch (JSDisconnectedException) { return false; }
        catch (JSException) { return false; }
        catch (TaskCanceledException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
