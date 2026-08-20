// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Common.Events;

public readonly record struct ProviderResolutionCounts(int Total, int Resolved, int NoProvider, int NoMessage, int Failed)
{
    public int Unresolved => NoProvider + NoMessage + Failed;

    public ProviderResolutionCounts Add(ProviderResolutionCounts other) => new(
        Total + other.Total,
        Resolved + other.Resolved,
        NoProvider + other.NoProvider,
        NoMessage + other.NoMessage,
        Failed + other.Failed);

    public ProviderResolutionCounts WithStatus(EventResolutionStatus status) => status switch
    {
        EventResolutionStatus.NoProvider => this with { Total = Total + 1, NoProvider = NoProvider + 1 },
        EventResolutionStatus.NoMessage => this with { Total = Total + 1, NoMessage = NoMessage + 1 },
        EventResolutionStatus.Failed => this with { Total = Total + 1, Failed = Failed + 1 },
        _ => this with { Total = Total + 1, Resolved = Resolved + 1 }
    };
}
