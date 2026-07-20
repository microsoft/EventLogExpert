// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Scenarios;

public interface IChannelReadinessService
{
    Task<ImmutableArray<ChannelReadiness>> GetReadinessAsync(CancellationToken cancellationToken = default);

    Task<ImmutableArray<ChannelReadiness>> GetReadinessAsync(
        IEnumerable<string> channels,
        CancellationToken cancellationToken = default);

    void Invalidate();
}
