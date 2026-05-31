// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EventLogExpert.UI.Focus;

internal static class ElementFocus
{
    public static async ValueTask SafelyAsync(ElementReference target)
    {
        try
        {
            await target.FocusAsync();
        }
        catch (ObjectDisposedException) { }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
    }
}
