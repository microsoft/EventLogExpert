// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Concurrency;

internal static class CooperativeCancellation
{
    internal const int CancellationCheckMask = CancellationCheckStride - 1;
    internal const int CancellationCheckStride = 8192;
}
