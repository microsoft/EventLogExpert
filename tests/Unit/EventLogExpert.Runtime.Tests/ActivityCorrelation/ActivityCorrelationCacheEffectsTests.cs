// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.ActivityCorrelation;
using NSubstitute;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Runtime.Tests.ActivityCorrelation;

public sealed class ActivityCorrelationCacheEffectsTests
{
    [Fact]
    public async Task HandleCloseAllLogs_InvalidatesTheCache()
    {
        var control = Substitute.For<IActivityCorrelationCacheControl>();
        var effects = new ActivityCorrelationCacheEffects(control);

        await effects.HandleCloseAllLogs(Substitute.For<IDispatcher>());

        control.Received(1).Invalidate();
    }

    [Fact]
    public async Task HandleCloseLog_InvalidatesTheCache()
    {
        var control = Substitute.For<IActivityCorrelationCacheControl>();
        var effects = new ActivityCorrelationCacheEffects(control);

        await effects.HandleCloseLog(Substitute.For<IDispatcher>());

        control.Received(1).Invalidate();
    }
}
