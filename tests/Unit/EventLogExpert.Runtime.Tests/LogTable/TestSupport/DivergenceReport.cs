// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Tests.LogTable.TestSupport;

internal static class DivergenceReport
{
    private const int MaxReported = 25;

    internal static string Describe(IReadOnlyList<string> failures) =>
        $"{failures.Count} divergence(s):{Environment.NewLine}{string.Join(Environment.NewLine, failures.Take(MaxReported))}";
}
