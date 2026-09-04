// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Menu;
using Microsoft.AspNetCore.Components.Web;

namespace EventLogExpert.UI.Tests.Menu;

public sealed class MenuButtonActivationTests
{
    [Fact]
    public void WasKeyboardTriggered_WithSingleClickDetail_IsFalse() =>
        Assert.False(MenuButtonActivation.WasKeyboardTriggered(new MouseEventArgs { Detail = 1 }));

    [Fact]
    public void WasKeyboardTriggered_WithZeroDetail_IsTrue() =>
        Assert.True(MenuButtonActivation.WasKeyboardTriggered(new MouseEventArgs { Detail = 0 }));
}
