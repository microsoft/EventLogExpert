// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.DatabaseTools.Elevation;

namespace EventLogExpert.Runtime.Tests.DatabaseTools.Elevation.TestUtils;

internal sealed class FakeElevatedHelperProcessHost(
    Func<IReadOnlyList<string>, CancellationToken, Task<IElevatedHelperProcess>> startFunc) : IElevatedHelperProcessHost
{
    public Task<IElevatedHelperProcess> StartAsync(IReadOnlyList<string> extraArgs, CancellationToken cancellationToken) =>
        startFunc(extraArgs, cancellationToken);
}
