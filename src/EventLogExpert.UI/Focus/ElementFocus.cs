// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EventLogExpert.UI.Focus;

// Focus helpers that swallow the transient interop failures a best-effort focus restore can hit (teardown, circuit
// loss, cancellation) plus InvalidOperationException, which ElementReference.FocusAsync throws for a default or
// never-captured reference - every caller treats a failed restore as "do nothing".
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
