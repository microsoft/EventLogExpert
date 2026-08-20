// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.LogTable;

namespace EventLogExpert.Runtime.ResolutionCoverage;

public interface IResolutionCoverageService
{
    ResolutionCoverageReport Build(IEventColumnView view, CancellationToken cancellationToken);

    ProviderCoverageDetail BuildProviderDetail(IEventColumnView view, string provider, CancellationToken cancellationToken);
}
