// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Menu;

namespace EventLogExpert.Runtime.Tests.Menu;

public sealed class OpenLogsBatchResultTests
{
    [Fact]
    public void AnyOpened_IsFalse_WhenNothingOpened()
    {
        Assert.False(new OpenLogsBatchResult(0, 3, 1, 2, ["a", "b", "c"]).AnyOpened);
        Assert.False(OpenLogsBatchResult.None.AnyOpened);
    }

    [Fact]
    public void AnyOpened_IsTrue_WhenOpenedIsPositive()
    {
        Assert.True(new OpenLogsBatchResult(1, 0, 0, 0, []).AnyOpened);
    }
}
