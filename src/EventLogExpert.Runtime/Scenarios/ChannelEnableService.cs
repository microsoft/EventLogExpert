// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Writers;
using EventLogExpert.Runtime.Common.Identity;

namespace EventLogExpert.Runtime.Scenarios;

internal sealed class ChannelEnableService(
    IChannelConfigWriter channelConfigWriter,
    IChannelReadinessService readinessService,
    IWindowsIdentityProvider identityProvider) : IChannelEnableService
{
    private readonly Lazy<bool> _canEnable = new(identityProvider.IsUserInAdministratorRole);

    public bool CanEnable => _canEnable.Value;

    public async Task<ChannelEnableResult> EnableAsync(string channel, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);

        // Do not attempt a write we know will be denied; a non-elevated caller never reaches the native layer.
        if (!CanEnable)
        {
            return new ChannelEnableResult(ChannelEnableOutcome.NotElevated, 0);
        }

        // Cancellation is honored only up to the moment the native write begins: the token guards both the
        // synchronous entry and the Task.Run scheduling boundary, so a cancel that arrives before the writer runs
        // prevents the write. Once EnableChannel starts it runs to completion (Task.Run stops consulting the token
        // once the delegate is executing), so a committed machine change is never reported as cancelled.
        cancellationToken.ThrowIfCancellationRequested();

        var result = await Task.Run(() => channelConfigWriter.EnableChannel(channel), cancellationToken)
            .ConfigureAwait(false);

        if (result.Outcome is ChannelEnableOutcome.Enabled or ChannelEnableOutcome.AlreadyEnabled)
        {
            readinessService.Invalidate();
        }

        return result;
    }
}
