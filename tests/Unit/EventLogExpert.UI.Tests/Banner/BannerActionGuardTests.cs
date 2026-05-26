// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using EventLogExpert.UI.Banner;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Banner;

public sealed class BannerActionGuardTests
{
    [Fact]
    public async Task RunSafelyAsync_ActionThrows_LogsExceptionWithComponentAndHandlerNames_DoesNotPropagate()
    {
        var logger = Substitute.For<ITraceLogger>();
        var actionException = new InvalidOperationException("action boom");

        await BannerActionGuard.RunSafelyAsync(
            () => throw actionException,
            logger,
            "componentName",
            "handlerName");

        logger.Received(1).Error(Arg.Is<ErrorLogHandler>(h =>
            h.ToString().Contains("componentName.handlerName: threw:") &&
            h.ToString().Contains("action boom")));
    }

    [Fact]
    public async Task RunSafelyAsync_HappyPath_InvokesAction_DoesNotLog()
    {
        var logger = Substitute.For<ITraceLogger>();
        int actionInvocationCount = 0;

        await BannerActionGuard.RunSafelyAsync(
            () =>
            {
                actionInvocationCount++;
                return Task.CompletedTask;
            },
            logger,
            "componentName",
            "handlerName");

        Assert.Equal(1, actionInvocationCount);
        logger.DidNotReceive().Error(Arg.Any<ErrorLogHandler>());
    }

    [Fact]
    public async Task RunSafelyAsync_NullActionOrLogger_ThrowsArgumentNullException()
    {
        var logger = Substitute.For<ITraceLogger>();

        var actionException = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BannerActionGuard.RunSafelyAsync(null!, logger, "componentName", "handlerName"));
        var loggerException = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BannerActionGuard.RunSafelyAsync(() => Task.CompletedTask, null!, "componentName", "handlerName"));

        Assert.Equal("action", actionException.ParamName);
        Assert.Equal("logger", loggerException.ParamName);
    }
}
