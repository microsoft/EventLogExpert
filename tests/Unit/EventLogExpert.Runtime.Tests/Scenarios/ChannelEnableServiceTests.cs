// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Writers;
using EventLogExpert.Runtime.Common.Identity;
using EventLogExpert.Runtime.Scenarios;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.Scenarios;

public sealed class ChannelEnableServiceTests
{
    private readonly IWindowsIdentityProvider _identity = Substitute.For<IWindowsIdentityProvider>();
    private readonly IChannelReadinessService _readiness = Substitute.For<IChannelReadinessService>();
    private readonly IChannelConfigWriter _writer = Substitute.For<IChannelConfigWriter>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void CanEnable_ReflectsAdministratorRole()
    {
        _identity.IsUserInAdministratorRole().Returns(true);

        Assert.True(Service().CanEnable);
    }

    [Fact]
    public async Task EnableAsync_WhenAlreadyEnabled_InvalidatesReadiness()
    {
        _identity.IsUserInAdministratorRole().Returns(true);
        _writer.EnableChannel("Microsoft-Windows-Sample/Operational")
            .Returns(new ChannelEnableResult(ChannelEnableOutcome.AlreadyEnabled, 0));

        var result = await Service().EnableAsync("Microsoft-Windows-Sample/Operational", Ct);

        Assert.Equal(ChannelEnableOutcome.AlreadyEnabled, result.Outcome);
        _readiness.Received(1).Invalidate();
    }

    [Fact]
    public async Task EnableAsync_WhenCancelledAfterSaveCommits_StillReturnsEnabledAndInvalidates()
    {
        _identity.IsUserInAdministratorRole().Returns(true);
        using var cts = new CancellationTokenSource();

        // The commit lands, then cancellation arrives during the native call; the service must not report the
        // committed change as cancelled, and must still invalidate.
        _writer.EnableChannel("Microsoft-Windows-Sample/Operational").Returns(_ =>
        {
            cts.Cancel();

            return new ChannelEnableResult(ChannelEnableOutcome.Enabled, 0);
        });

        var result = await Service().EnableAsync("Microsoft-Windows-Sample/Operational", cts.Token);

        Assert.Equal(ChannelEnableOutcome.Enabled, result.Outcome);
        _readiness.Received(1).Invalidate();
    }

    [Fact]
    public async Task EnableAsync_WhenCancelledBeforeNativeOp_ThrowsWithoutWriting()
    {
        _identity.IsUserInAdministratorRole().Returns(true);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Service().EnableAsync("Microsoft-Windows-Sample/Operational", cts.Token));

        _writer.DidNotReceive().EnableChannel(Arg.Any<string>());
        _readiness.DidNotReceive().Invalidate();
    }

    [Fact]
    public async Task EnableAsync_WhenEnabled_InvalidatesReadiness()
    {
        _identity.IsUserInAdministratorRole().Returns(true);
        _writer.EnableChannel("Microsoft-Windows-Sample/Operational")
            .Returns(new ChannelEnableResult(ChannelEnableOutcome.Enabled, 0));

        var result = await Service().EnableAsync("Microsoft-Windows-Sample/Operational", Ct);

        Assert.Equal(ChannelEnableOutcome.Enabled, result.Outcome);
        _readiness.Received(1).Invalidate();
    }

    [Fact]
    public async Task EnableAsync_WhenNotElevated_ReturnsNotElevatedWithoutWritingOrInvalidating()
    {
        _identity.IsUserInAdministratorRole().Returns(false);

        var result = await Service().EnableAsync("Microsoft-Windows-Sample/Operational", Ct);

        Assert.Equal(ChannelEnableOutcome.NotElevated, result.Outcome);
        _writer.DidNotReceive().EnableChannel(Arg.Any<string>());
        _readiness.DidNotReceive().Invalidate();
    }

    [Theory]
    [InlineData(ChannelEnableOutcome.AccessDenied)]
    [InlineData(ChannelEnableOutcome.NotFound)]
    [InlineData(ChannelEnableOutcome.Failed)]
    public async Task EnableAsync_WhenWriteFails_DoesNotInvalidate(ChannelEnableOutcome outcome)
    {
        _identity.IsUserInAdministratorRole().Returns(true);
        _writer.EnableChannel("Microsoft-Windows-Sample/Operational")
            .Returns(new ChannelEnableResult(outcome, 5));

        var result = await Service().EnableAsync("Microsoft-Windows-Sample/Operational", Ct);

        Assert.Equal(outcome, result.Outcome);
        _readiness.DidNotReceive().Invalidate();
    }

    private ChannelEnableService Service() => new(_writer, _readiness, _identity);
}
