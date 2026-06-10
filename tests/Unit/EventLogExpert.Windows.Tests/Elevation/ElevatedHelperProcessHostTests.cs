// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.WindowsPlatform.Elevation;
using NSubstitute;
using System.IO.Pipes;
using Xunit;

namespace EventLogExpert.Windows.Tests.Elevation;

public sealed class ElevatedHelperProcessHostTests
{
    [Fact]
    public async Task AcceptAndVerifyClientPidAsync_WhenClientPidMatches_ReturnsWithoutError()
    {
        var ct = TestContext.Current.CancellationToken;
        var pipeName = $"eventlogexpert-test-{Guid.NewGuid():N}";
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        var logger = Substitute.For<ITraceLogger>();

        var acceptTask = ElevatedHelperProcessHost.AcceptAndVerifyClientPidAsync(
            server,
            expectedPid: Environment.ProcessId,
            logger,
            ct);

        using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await client.ConnectAsync(ct);

        await acceptTask.WaitAsync(TimeSpan.FromSeconds(5), ct);

        Assert.True(server.IsConnected);
    }

    [Fact]
    public async Task AcceptAndVerifyClientPidAsync_WhenClientPidMismatches_DisconnectsAndKeepsWaiting()
    {
        var ct = TestContext.Current.CancellationToken;
        var pipeName = $"eventlogexpert-test-{Guid.NewGuid():N}";
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        var logger = Substitute.For<ITraceLogger>();

        using var loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Expected PID = 0 never matches a real process; loop should disconnect after each connection and keep
        // waiting until the deadline cancels the loop.
        var acceptTask = ElevatedHelperProcessHost.AcceptAndVerifyClientPidAsync(
            server,
            expectedPid: 0,
            logger,
            loopCts.Token);

        using (var client1 = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await client1.ConnectAsync(ct);
        }

        await Task.Delay(50, ct);
        loopCts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acceptTask);
    }
}
