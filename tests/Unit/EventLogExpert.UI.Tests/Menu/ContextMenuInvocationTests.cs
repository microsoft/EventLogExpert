// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Menu;
using Microsoft.AspNetCore.Components.Web;

namespace EventLogExpert.UI.Tests.Menu;

public sealed class ContextMenuInvocationTests
{
    [Fact]
    public void WasKeyboardTriggered_WithNoButton_IsTrue() =>
        Assert.True(ContextMenuInvocation.WasKeyboardTriggered(new MouseEventArgs { Button = 0 }));

    [Fact]
    public void WasKeyboardTriggered_WithSecondaryMouseButton_IsFalse() =>
        Assert.False(ContextMenuInvocation.WasKeyboardTriggered(new MouseEventArgs { Button = 2 }));
}
