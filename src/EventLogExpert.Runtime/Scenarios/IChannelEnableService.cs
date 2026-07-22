// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Writers;

namespace EventLogExpert.Runtime.Scenarios;

public interface IChannelEnableService
{
    /// <summary>
    ///     Whether an enable can be ATTEMPTED (the process is elevated). A true value is not a guarantee that the write
    ///     will succeed - a channel-specific ACL can still deny an elevated caller.
    /// </summary>
    bool CanEnable { get; }

    Task<ChannelEnableResult> EnableAsync(string channel, CancellationToken cancellationToken = default);
}
