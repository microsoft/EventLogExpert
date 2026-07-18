// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Histogram;

namespace EventLogExpert.Runtime.Tests.Histogram;

public sealed class HistogramStateTests
{
    [Fact]
    public void IsVisible_DefaultsToHidden()
    {
        // The timeline is off on every launch - there is no persisted preference; a scenario or the View menu reveals it.
        Assert.False(new HistogramState().IsVisible);
    }
}
